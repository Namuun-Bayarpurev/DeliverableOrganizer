using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DeliverableOrganizer.Services;

/// <summary>PAT の保存先（「保存する」チェック）。</summary>
public interface IPatStore
{
    /// <summary>保存済みの PAT。なければ <see langword="null"/>。</summary>
    string? Load();

    void Save(string pat);

    /// <summary>保存済みの PAT を消す。なければ何もしない。</summary>
    void Delete();
}

/// <summary>
/// Windows 資格情報マネージャー（汎用資格情報）に PAT を保存する。
/// ログオン中のユーザーだけが読め、このマシンに残る（CRED_PERSIST_LOCAL_MACHINE）。
/// </summary>
/// <param name="target">資格情報の名前（資格情報マネージャーの一覧に出る名前）。</param>
public sealed class WindowsCredentialPatStore(string target = WindowsCredentialPatStore.DefaultTarget) : IPatStore
{
    public const string DefaultTarget = "DeliverableOrganizer:AzureDevOpsPat";

    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? Load()
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var ptr))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public void Save(string pat)
    {
        var bytes = Encoding.UTF8.GetBytes(pat);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "PAT",
                Comment = "納品物整理ツール: Azure DevOps の PAT",
            };
            if (!CredWrite(ref cred, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            // 平文の PAT をメモリに残さない
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete()
    {
        if (!CredDelete(target, CredTypeGeneric, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
