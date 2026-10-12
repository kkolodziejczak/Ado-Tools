// Adapted from Git Credential Manager (https://github.com/git-ecosystem/git-credential-manager), commit 91d6da2f699d60802cdd5d339ba2b91b38dce549, MIT License.
// Source: src/Core/Interop/Linux/SecretServiceCollection.cs. Reduced to one Secret Service item per
// account. Attribute tables carry no managed callbacks, and the item is unlocked before it is read, as in GCM.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AdoRead.Configuration;
using AdoRead.Credentials.Native;
using static AdoRead.Credentials.Native.Libsecret;

namespace AdoRead.Credentials;

internal sealed class SecretServiceVault : ICredentialVault
{
    private const string VaultName = "Secret Service";
    private const string SchemaName = "ado-read";
    private const string ServiceAttributeName = "service";
    private const string AccountAttributeName = "account";
    private const string PlainTextContentType = "text/plain";

    public string Kind => "linuxSecretService";

    public string? Read(string account)
    {
        try
        {
            return ReadToken(account);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw LibraryUnavailable();
        }
    }

    public void Write(string account, string secret)
    {
        try
        {
            WriteToken(account, secret);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw LibraryUnavailable();
        }
    }

    public bool Remove(string account)
    {
        try
        {
            return RemoveToken(account);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw LibraryUnavailable();
        }
    }

    private static string? ReadToken(string account)
    {
        IntPtr service = OpenService();
        try
        {
            using var strings = new NativeStrings();
            IntPtr items = SearchItems(service, strings, account);
            try
            {
                return items == IntPtr.Zero ? null : SecretOfFirstItem(service, items);
            }
            finally
            {
                FreeItems(items);
            }
        }
        finally
        {
            Gobject.g_object_unref(service);
        }
    }

    private static void WriteToken(string account, string secret)
    {
        IntPtr service = OpenService();
        try
        {
            using var strings = new NativeStrings();
            StoreItem(service, strings, account, secret);
        }
        finally
        {
            Gobject.g_object_unref(service);
        }
    }

    private static bool RemoveToken(string account)
    {
        IntPtr service = OpenService();
        try
        {
            using var strings = new NativeStrings();
            IntPtr items = SearchItems(service, strings, account);
            bool found = items != IntPtr.Zero;
            FreeItems(items);
            return found && ClearItem(service, strings, account);
        }
        finally
        {
            Gobject.g_object_unref(service);
        }
    }

    private static IntPtr OpenService()
    {
        IntPtr service = secret_service_get_sync(
            SECRET_SERVICE_OPEN_SESSION | SECRET_SERVICE_LOAD_COLLECTIONS,
            IntPtr.Zero,
            out IntPtr error);
        if (error != IntPtr.Zero)
        {
            throw CredentialStoreErrors.Unavailable(
                VaultName,
                $"start a Secret Service provider such as gnome-keyring. The service reported: {TakeErrorMessage(error)}");
        }

        if (service == IntPtr.Zero)
        {
            throw CredentialStoreErrors.Unavailable(VaultName, "the Secret Service opened no session.");
        }

        return service;
    }

    private static IntPtr SearchItems(IntPtr service, NativeStrings strings, string account)
    {
        IntPtr attributes = CreateAttributes(strings, account);
        try
        {
            SecretSchema schema = Schema();
            IntPtr items = secret_service_search_sync(
                service,
                ref schema,
                attributes,
                SECRET_SEARCH_ALL | SECRET_SEARCH_UNLOCK,
                IntPtr.Zero,
                out IntPtr error);
            if (error != IntPtr.Zero)
            {
                throw Failed("read", TakeErrorMessage(error));
            }

            return items;
        }
        finally
        {
            Glib.g_hash_table_destroy(attributes);
        }
    }

    private static string SecretOfFirstItem(IntPtr service, IntPtr items)
    {
        IntPtr item = Marshal.PtrToStructure<Glib.GList>(items).data;
        if (secret_item_get_locked(item))
        {
            UnlockItem(service, item);
        }

        secret_item_load_secret_sync(item, IntPtr.Zero, out IntPtr error);
        if (error != IntPtr.Zero)
        {
            throw Failed("read", TakeErrorMessage(error));
        }

        IntPtr secretValue = secret_item_get_secret(item);
        if (secretValue == IntPtr.Zero)
        {
            throw Failed("read", "the item has no secret.");
        }

        try
        {
            return SecretText(secretValue);
        }
        finally
        {
            secret_value_unref(secretValue);
        }
    }

    private static void UnlockItem(IntPtr service, IntPtr item)
    {
        IntPtr list = Marshal.AllocHGlobal(Marshal.SizeOf<Glib.GList>());
        try
        {
            Marshal.StructureToPtr(new Glib.GList { data = item }, list, false);
            int unlocked = secret_service_unlock_sync(service, list, IntPtr.Zero, out IntPtr unlockedList, out IntPtr error);
            // The unlocked list holds the same items as the search results, which are released later, so only its nodes are freed here.
            Glib.g_list_free_full(unlockedList, IntPtr.Zero);
            if (error != IntPtr.Zero)
            {
                throw Failed("read", TakeErrorMessage(error));
            }

            if (unlocked != 1)
            {
                throw Failed("read", "the item is locked and could not be unlocked.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(list);
        }
    }

    private static void StoreItem(IntPtr service, NativeStrings strings, string account, string secret)
    {
        byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
        IntPtr attributes = CreateAttributes(strings, account);
        IntPtr value = secret_value_new(secretBytes, secretBytes.Length, PlainTextContentType);
        try
        {
            SecretSchema schema = Schema();
            bool stored = secret_service_store_sync(
                service,
                ref schema,
                attributes,
                null,
                $"{CredentialVault.ServiceName}:{account}",
                value,
                IntPtr.Zero,
                out IntPtr error);
            if (error != IntPtr.Zero)
            {
                throw Failed("write", TakeErrorMessage(error));
            }

            if (!stored)
            {
                throw Failed("write", "the Secret Service did not confirm the item.");
            }
        }
        finally
        {
            secret_value_unref(value);
            Glib.g_hash_table_destroy(attributes);
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    private static bool ClearItem(IntPtr service, NativeStrings strings, string account)
    {
        IntPtr attributes = CreateAttributes(strings, account);
        try
        {
            SecretSchema schema = Schema();
            bool cleared = secret_service_clear_sync(service, ref schema, attributes, IntPtr.Zero, out IntPtr error);
            if (error != IntPtr.Zero)
            {
                throw Failed("remove", TakeErrorMessage(error));
            }

            return cleared;
        }
        finally
        {
            Glib.g_hash_table_destroy(attributes);
        }
    }

    private static IntPtr CreateAttributes(NativeStrings strings, string account)
    {
        IntPtr table = Glib.g_hash_table_new_full(
            Glib.Export("g_str_hash"),
            Glib.Export("g_str_equal"),
            IntPtr.Zero,
            IntPtr.Zero);
        Glib.g_hash_table_insert(table, strings.Add(ServiceAttributeName), strings.Add(CredentialVault.ServiceName));
        Glib.g_hash_table_insert(table, strings.Add(AccountAttributeName), strings.Add(account));
        return table;
    }

    private static SecretSchema Schema()
    {
        var schema = new SecretSchema
        {
            name = SchemaName,
            flags = SECRET_SCHEMA_DONT_MATCH_NAME,
            attributes = new SecretSchemaAttribute[32]
        };
        schema.attributes[0] = new SecretSchemaAttribute { name = ServiceAttributeName, type = SECRET_SCHEMA_ATTRIBUTE_STRING };
        schema.attributes[1] = new SecretSchemaAttribute { name = AccountAttributeName, type = SECRET_SCHEMA_ATTRIBUTE_STRING };
        return schema;
    }

    private static string SecretText(IntPtr secretValue)
    {
        IntPtr data = secret_value_get(secretValue, out nint length);
        byte[] bytes = new byte[(int)length];
        Marshal.Copy(data, bytes, 0, bytes.Length);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void FreeItems(IntPtr items)
    {
        if (items != IntPtr.Zero)
        {
            Glib.g_list_free_full(items, Gobject.UnrefFunction);
        }
    }

    private static string TakeErrorMessage(IntPtr error)
    {
        try
        {
            Glib.GError details = Marshal.PtrToStructure<Glib.GError>(error);
            return Marshal.PtrToStringUTF8(details.message) ?? "unknown error";
        }
        finally
        {
            Glib.g_error_free(error);
        }
    }

    private static CredentialException LibraryUnavailable() =>
        CredentialStoreErrors.Unavailable(
            VaultName,
            "libsecret-1 is not installed. Install libsecret and a Secret Service provider such as gnome-keyring.");

    private static CredentialException Failed(string operation, string reason) =>
        CredentialStoreErrors.Failed(VaultName, operation, reason);

    /// <summary>Owns the native copies of attribute strings until the Secret Service call returns.</summary>
    private sealed class NativeStrings : IDisposable
    {
        private readonly List<IntPtr> _strings = [];

        public IntPtr Add(string value)
        {
            IntPtr ptr = Marshal.StringToCoTaskMemUTF8(value);
            _strings.Add(ptr);
            return ptr;
        }

        public void Dispose()
        {
            foreach (IntPtr ptr in _strings)
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
    }
}
