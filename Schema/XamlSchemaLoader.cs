// Copyright © 2026 Virich Pavlo. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Newtonsoft.Json;

namespace A2v10XamlAutocomplete;

#region Raw JSON model

internal sealed class RawSchema
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonProperty("platformVersion")]
    public string PlatformVersion { get; set; }

    [JsonProperty("elements")]
    public Dictionary<string, RawElementInfo> Elements { get; set; }

    [JsonProperty("enums")]
    public Dictionary<string, List<string>> Enums { get; set; }

    [JsonProperty("allowedChildElements")]
    public Dictionary<string, List<string>> AllowedChildElements { get; set; }

    [JsonProperty("attachedProperties")]
    public Dictionary<string, RawAttachedPropertyInfo> AttachedProperties { get; set; }
}

internal sealed class RawElementInfo
{
    [JsonProperty("elementClrType")]
    public string ElementClrType { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("contentProperty")]
    public string ContentProperty { get; set; }

    [JsonProperty("contentAsXamlAttr")]
    public bool ContentAsXamlAttr { get; set; }

    [JsonProperty("properties")]
    public Dictionary<string, RawPropertyInfo> Properties { get; set; }
}

internal sealed class RawPropertyInfo
{
    [JsonProperty("declaredType")]
    public string DeclaredType { get; set; }

    [JsonProperty("isEnum")]
    public bool IsEnum { get; set; }

    [JsonProperty("isElement")]
    public bool IsElement { get; set; }

    [JsonProperty("isCollection")]
    public bool IsCollection { get; set; }

    [JsonProperty("collectionItemType")]
    public string CollectionItemType { get; set; }

    [JsonProperty("canBeAttribute")]
    public bool CanBeAttribute { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }
}

internal sealed class RawAttachedPropertyInfo
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("isEnum")]
    public bool IsEnum { get; set; }

    [JsonProperty("canBeAttribute")]
    public bool CanBeAttribute { get; set; }

    [JsonProperty("applicableTags")]
    public List<string> ApplicableTags { get; set; }
}

#endregion

internal static class XamlSchemaLoader
{
    private const string ResourceName =
        "A2v10XamlAutocomplete.Resources.a2v10-xaml-schema.json";
    private const int SupportedSchemaVersion = 2;

    public static RawSchema LoadFromResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream(ResourceName))
        {
            if (stream == null)
                throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' not found. " +
                    $"Available: {String.Join(", ", assembly.GetManifestResourceNames())}");

            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var schema = JsonConvert.DeserializeObject<RawSchema>(json);

                if (schema == null)
                    throw new InvalidOperationException(
                        "Failed to deserialize XAML schema");

                if (schema.SchemaVersion < 1
                    || schema.SchemaVersion > SupportedSchemaVersion)
                    throw new InvalidOperationException(
                        $"Schema version {schema.SchemaVersion} is not supported. " +
                        $"Supported: 1..{SupportedSchemaVersion}");

                return schema;
            }
        }
    }
}
