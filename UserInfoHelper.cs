using System;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace GridBanner
{
    /// <summary>
    /// Helper class to get user information including Azure AD organization name.
    /// </summary>
    public class UserInfoHelper
    {
        public static Dictionary<string, string> GetUserInfo()
        {
            return new Dictionary<string, string>
            {
                { "username", Environment.UserName },
                { "org_name", GetAzureAdOrgName() }
            };
        }

        private static string GetAzureAdOrgName()
        {
            // 1) Try Azure AD tenant display name from registry (best for AAD joined machines)
            // HKCU\Software\Microsoft\Windows\CurrentVersion\AAD\TenantInfo\{tenantId}\DisplayName
            try
            {
                using var tenantInfo = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\AAD\TenantInfo");
                if (tenantInfo != null)
                {
                    foreach (var subKeyName in tenantInfo.GetSubKeyNames())
                    {
                        using var sub = tenantInfo.OpenSubKey(subKeyName);
                        var displayName = sub?.GetValue("DisplayName") as string;
                        if (!string.IsNullOrWhiteSpace(displayName))
                        {
                            return displayName.Trim();
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            // 2) Try CDJ AAD info (some builds store TenantName here)
            try
            {
                using var cdjAad = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\CDJ\AAD");
                var tenantName = cdjAad?.GetValue("TenantName") as string;
                if (!string.IsNullOrWhiteSpace(tenantName))
                {
                    return tenantName.Trim();
                }
            }
            catch
            {
                // ignore
            }

            // 3) Try parsing dsregcmd output for TenantName
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dsregcmd.exe",
                    Arguments = "/status",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(2000);

                    using var reader = new StringReader(output);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Example line: "TenantName : PrecisionX Technology LLC"
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("TenantName", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = trimmed.IndexOf(':');
                            if (idx >= 0)
                            {
                                var value = trimmed[(idx + 1)..].Trim();
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    return value;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            // Try USERDNSDOMAIN first (most reliable for domain-joined machines)
            var orgName = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
            if (!string.IsNullOrEmpty(orgName))
            {
                // Extract just the domain name (remove user part if present)
                var parts = orgName.Split('.');
                return parts.Length > 0 ? parts[0].ToUpper() : orgName.ToUpper();
            }

            // Try USERDOMAIN
            orgName = Environment.GetEnvironmentVariable("USERDOMAIN");
            if (!string.IsNullOrEmpty(orgName))
            {
                return orgName.ToUpper();
            }

            // Try LOGONSERVER
            var logonServer = Environment.GetEnvironmentVariable("LOGONSERVER");
            if (!string.IsNullOrEmpty(logonServer))
            {
                // Remove leading backslashes
                orgName = logonServer.TrimStart('\\', '/');
                if (!string.IsNullOrEmpty(orgName))
                {
                    return orgName.ToUpper();
                }
            }

            // Try Windows Registry for domain information
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                if (key != null)
                {
                    var domainName = key.GetValue("Domain") as string;
                    if (!string.IsNullOrEmpty(domainName))
                    {
                        var parts = domainName.Split('.');
                        return parts.Length > 0 ? parts[0].ToUpper() : domainName.ToUpper();
                    }
                }
            }
            catch
            {
                // Registry access failed, continue to next method
            }

            // Fallback to computer name
            var computerName = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (!string.IsNullOrEmpty(computerName))
            {
                return computerName.ToUpper();
            }

            // Final fallback
            return "ORGANIZATION";
        }
    }
}

