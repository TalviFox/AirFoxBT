using System;
using System.Text.RegularExpressions;

namespace AirFoxBT.Models;

public static class DeviceNaming
{
    public static (string Slug, string FriendlyName) ResolveNames(string? rawName)
    {
        string name = string.IsNullOrWhiteSpace(rawName) ? Environment.MachineName : rawName.Trim();

        // Friendly Name: e.g. "AirFoxBT (Server Rack)" or "AirFoxBT"
        string friendlyName;
        if (name.StartsWith("AirFoxBT", StringComparison.OrdinalIgnoreCase))
        {
            friendlyName = name;
        }
        else
        {
            friendlyName = $"AirFoxBT ({name})";
        }

        // Slug: e.g. "airfoxbt-server-rack" or "airfoxbt-multifoxmini"
        string rawSlug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\-]+", "-").Trim('-');
        if (string.IsNullOrEmpty(rawSlug))
        {
            rawSlug = "node";
        }

        string slug;
        if (rawSlug.StartsWith("airfoxbt", StringComparison.OrdinalIgnoreCase))
        {
            slug = rawSlug;
        }
        else
        {
            slug = $"airfoxbt-{rawSlug}";
        }

        // Truncate to 31 characters per ESPHome validate_hostname specification (ESPHOME_DEVICE_NAME_MAX_LEN)
        if (slug.Length > 31)
        {
            slug = slug[..31].TrimEnd('-');
        }

        return (slug, friendlyName);
    }
}
