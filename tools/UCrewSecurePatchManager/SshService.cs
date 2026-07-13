using System.Reflection;
using Renci.SshNet;

namespace UCREW.SecurePatch.Manager;

internal static class SshService
{
    public static void TestConnection(ServerSettings settings, Action<string> log)
    {
        using SshClient ssh = CreateSsh(settings);
        ssh.Connect();
        string result = Execute(ssh, "echo U-CREW_SSH_OK && uname -a");
        log(result.Trim());
        ssh.Disconnect();
    }

    public static void InstallServer(ServerSettings settings, Action<string> log)
    {
        string tempRoot = ExtractEmbeddedServerFiles();
        string remoteRoot = $"/root/ucrew_manager_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

        try
        {
            using SftpClient sftp = CreateSftp(settings);
            using SshClient ssh = CreateSsh(settings);
            sftp.Connect();
            ssh.Connect();

            Execute(ssh, $"mkdir -p '{remoteRoot}/api' '{remoteRoot}/database'");
            UploadFile(sftp, Path.Combine(tempRoot, "secure_patch_request.php"), remoteRoot + "/api/secure_patch_request.php", log);
            UploadFile(sftp, Path.Combine(tempRoot, "secure_patch_download.php"), remoteRoot + "/api/secure_patch_download.php", log);
            UploadFile(sftp, Path.Combine(tempRoot, "INSTALL_SCHEMA.sql"), remoteRoot + "/database/INSTALL_SCHEMA.sql", log);
            UploadFile(sftp, Path.Combine(tempRoot, "INSTALL_VPS.sh"), remoteRoot + "/INSTALL_VPS.sh", log);

            log("Sunucu kurulum betiği çalıştırılıyor...");
            string installOutput = Execute(ssh,
                $"chmod +x '{remoteRoot}/INSTALL_VPS.sh' && APP_ROOT='{EscapeShell(settings.AppRoot)}' bash '{remoteRoot}/INSTALL_VPS.sh'");
            log(installOutput.Trim());

            log("MySQL şeması uygulanıyor...");
            string db = ValidateDatabaseName(settings.DatabaseName);
            string schemaOutput = Execute(ssh,
                $"mysql '{db}' < '{remoteRoot}/database/INSTALL_SCHEMA.sql'");
            if (!string.IsNullOrWhiteSpace(schemaOutput))
            {
                log(schemaOutput.Trim());
            }

            string apiTest = Execute(ssh,
                "curl -sS -X POST https://api.u-crew.net/api/secure_patch_request.php -d slug=guardians -d hwid=test");
            log("API testi: " + apiTest.Trim());

            sftp.Disconnect();
            ssh.Disconnect();
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public static void Publish(
        ServerSettings settings,
        PackageResult package,
        Action<string> log)
    {
        string slug = ValidateSlug(package.GameSlug);
        string database = ValidateDatabaseName(settings.DatabaseName);
        string remoteDirectory = $"{settings.AppRoot.TrimEnd('/')}/secure_patches/{slug}";
        string remotePackage = remoteDirectory + "/" + Path.GetFileName(package.EncryptedPackagePath);
        string remoteSql = $"/root/ucrew_register_{slug}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sql";

        using SftpClient sftp = CreateSftp(settings);
        using SshClient ssh = CreateSsh(settings);
        sftp.Connect();
        ssh.Connect();

        Execute(ssh, $"install -d -o www-data -g www-data -m 0750 '{remoteDirectory}'");
        UploadFile(sftp, package.EncryptedPackagePath, remotePackage, log);

        log("Sunucuda SHA-256 doğrulanıyor...");
        string actualSha = Execute(ssh, $"sha256sum '{remotePackage}' | awk '{{print $1}}'").Trim();
        if (!string.Equals(actualSha, package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Execute(ssh, $"rm -f '{remotePackage}'");
            throw new InvalidDataException("Sunucu SHA-256 doğrulaması başarısız.");
        }

        Execute(ssh, $"chown www-data:www-data '{remotePackage}' && chmod 0640 '{remotePackage}'");
        UploadFile(sftp, package.SqlPath, remoteSql, log);

        log("Yama veritabanına kaydediliyor...");
        string sqlOutput = Execute(ssh,
            $"set -e; mysql '{database}' < '{remoteSql}'; rm -f '{remoteSql}'");
        if (!string.IsNullOrWhiteSpace(sqlOutput))
        {
            log(sqlOutput.Trim());
        }

        string verifySql =
            $"SELECT g.slug,f.version,f.channel,f.file_name,f.sha256,f.status " +
            $"FROM secure_patch_files f INNER JOIN games g ON g.id=f.game_id " +
            $"WHERE LOWER(g.slug)=LOWER('{slug}') ORDER BY f.id DESC LIMIT 1;";
        string verify = Execute(ssh,
            $"mysql -N -B '{database}' -e \"{verifySql}\"");
        log("Kayıt doğrulaması: " + verify.Trim());

        sftp.Disconnect();
        ssh.Disconnect();
    }

    private static string ExtractEmbeddedServerFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "UCREW_Server_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assembly assembly = Assembly.GetExecutingAssembly();

        ExtractBySuffix(assembly, ".secure_patch_request.php", Path.Combine(root, "secure_patch_request.php"));
        ExtractBySuffix(assembly, ".secure_patch_download.php", Path.Combine(root, "secure_patch_download.php"));
        ExtractBySuffix(assembly, ".INSTALL_SCHEMA.sql", Path.Combine(root, "INSTALL_SCHEMA.sql"));
        ExtractBySuffix(assembly, ".INSTALL_VPS.sh", Path.Combine(root, "INSTALL_VPS.sh"));
        return root;
    }

    private static void ExtractBySuffix(Assembly assembly, string suffix, string destination)
    {
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException("Gömülü sunucu kaynağı bulunamadı: " + suffix);
        }

        using Stream input = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Gömülü kaynak açılamadı: " + resourceName);
        using FileStream output = File.Create(destination);
        input.CopyTo(output);
    }

    private static void UploadFile(SftpClient sftp, string localPath, string remotePath, Action<string> log)
    {
        log("Yükleniyor: " + Path.GetFileName(localPath));
        using FileStream stream = File.OpenRead(localPath);
        sftp.UploadFile(stream, remotePath, canOverride: true);
    }

    private static SshClient CreateSsh(ServerSettings settings)
    {
        return new SshClient(settings.Host, settings.Port, settings.User, settings.Password)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15),
            ConnectionInfo = { Timeout = TimeSpan.FromSeconds(30) }
        };
    }

    private static SftpClient CreateSftp(ServerSettings settings)
    {
        return new SftpClient(settings.Host, settings.Port, settings.User, settings.Password)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15),
            ConnectionInfo = { Timeout = TimeSpan.FromSeconds(30) }
        };
    }

    private static string Execute(SshClient ssh, string commandText)
    {
        using SshCommand command = ssh.CreateCommand(commandText);
        command.CommandTimeout = TimeSpan.FromMinutes(5);
        string output = command.Execute();
        if (command.ExitStatus != 0)
        {
            string error = string.IsNullOrWhiteSpace(command.Error) ? output : command.Error;
            throw new InvalidOperationException($"Uzak komut başarısız ({command.ExitStatus}): {error.Trim()}");
        }
        return output;
    }

    private static string ValidateSlug(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z0-9][a-z0-9._-]{1,99}$"))
        {
            throw new InvalidDataException("Geçersiz oyun slug değeri.");
        }
        return value;
    }

    private static string ValidateDatabaseName(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_]+$"))
        {
            throw new InvalidDataException("Geçersiz veritabanı adı.");
        }
        return value;
    }

    private static string EscapeShell(string value) => value.Replace("'", "'\"'\"'");
}
