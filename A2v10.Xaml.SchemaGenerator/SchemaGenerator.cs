// Copyright © 2026 Virich Pavlo. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.XPath;

namespace A2v10.Xaml.SchemaGenerator;

internal sealed class SchemaGenerator
{
	private readonly Assembly _assembly;
	private readonly Type _xamlElementType;
	private readonly XPathNavigator? _xmlDoc;

	// Collected data
	private readonly Dictionary<string, ElementSchema> _elements = new();
	private readonly Dictionary<string, List<string>> _enums = new();
	private readonly Dictionary<string, List<string>> _allowedChildElements = new();
	private readonly Dictionary<string, AttachedPropertySchema> _attachedProperties = new();

	// All concrete element types: tagName -> Type
	private readonly Dictionary<string, Type> _elementTypesByTag = new();

	// All exported types indexed by formatted name for reverse lookup
	private readonly Dictionary<string, Type> _typeByFormattedName = new();

	// Known collection item types for non-generic collections
	private static readonly Dictionary<string, string> KnownCollectionItemTypes = new()
	{
		["A2v10.Xaml.UIElementCollection"] = "A2v10.Xaml.UIElementBase",
		["A2v10.Xaml.InlineCollection"] = "System.Object",
	};

	public SchemaGenerator(Assembly assembly)
	{
		_assembly = assembly;

		_xamlElementType = FindType("XamlElement")
			?? throw new InvalidOperationException(
				"Type 'XamlElement' not found in the assembly.");

		_xmlDoc = LoadXmlDoc(assembly);
	}

	public string Generate()
	{
		var allTypes = _assembly.GetExportedTypes();

		DiscoverElementTypes(allTypes);
		ProcessElements();
		BuildAllowedChildElements();
		DiscoverAttachedProperties(allTypes);

		var schema = new
		{
			schemaVersion = 2,
			platformVersion = _assembly.GetName().Version?.ToString() ?? "0.0.0",
			elements = _elements,
			enums = _enums,
			allowedChildElements = _allowedChildElements,
			attachedProperties = _attachedProperties
		};

		var options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		return JsonSerializer.Serialize(schema, options);
	}

	#region Element Discovery

	private void DiscoverElementTypes(Type[] allTypes)
	{
		// Build formatted name index for all exported types
		foreach (var type in allTypes)
		{
			string formatted = FormatTypeName(type);
			_typeByFormattedName[formatted] = type;
		}

		foreach (var type in allTypes)
		{
			if (!_xamlElementType.IsAssignableFrom(type))
				continue;
			if (type.IsAbstract || !type.IsPublic)
				continue;
			if (HasAttribute(type, "ObsoleteAttribute"))
				continue;
			if (IsBrowsableFalse(type))
				continue;

			string tagName = GetTagName(type);

			if (_elementTypesByTag.TryGetValue(tagName, out var existing))
			{
				// Resolve conflict: type in root namespace A2v10.Xaml wins,
				// the other gets a sub-namespace prefix (e.g. "Drawing.Card").
				string rootNs = "A2v10.Xaml";
				bool existingIsRoot = existing.Namespace == rootNs;
				bool newIsRoot = type.Namespace == rootNs;

				if (existingIsRoot && !newIsRoot)
				{
					string qualifiedTag = GetSubNamespacePrefix(type, rootNs) + tagName;
					Console.WriteLine(
						$"  Tag conflict: '{tagName}' -> keeping '{existing.FullName}', " +
						$"adding '{type.FullName}' as '{qualifiedTag}'");
					_elementTypesByTag[qualifiedTag] = type;
				}
				else if (!existingIsRoot && newIsRoot)
				{
					string qualifiedTag = GetSubNamespacePrefix(existing, rootNs) + tagName;
					Console.WriteLine(
						$"  Tag conflict: '{tagName}' -> replacing with '{type.FullName}', " +
						$"moving '{existing.FullName}' to '{qualifiedTag}'");
					_elementTypesByTag[tagName] = type;
					_elementTypesByTag[qualifiedTag] = existing;
				}
				else
				{
					Console.WriteLine(
						$"  WARNING: Tag conflict '{tagName}' between " +
						$"'{existing.FullName}' and '{type.FullName}' — skipping latter");
				}
				continue;
			}

			_elementTypesByTag[tagName] = type;
		}

		Console.WriteLine($"Discovered {_elementTypesByTag.Count} element types.");
	}

	private void ProcessElements()
	{
		foreach (var (tagName, type) in _elementTypesByTag)
		{
			var element = new ElementSchema
			{
				ElementClrType = FormatTypeName(type)
			};

			string? contentProp = GetContentPropertyName(type);
			if (contentProp != null)
				element.ContentProperty = contentProp;

			if (HasAttributeInHierarchy(type, "ContentAsXamlAttrAttribute"))
				element.ContentAsXamlAttr = true;

			string? description = GetXmlDocSummary(type);
			if (description != null)
				element.Description = description;

			element.Properties = CollectProperties(type);

			_elements[tagName] = element;
		}

		Console.WriteLine($"Processed {_elements.Count} elements, " +
			$"{_enums.Count} enum types discovered.");
	}

	private Dictionary<string, PropertySchema> CollectProperties(Type elementType)
	{
		var result = new Dictionary<string, PropertySchema>();
		var properties = elementType.GetProperties(
			BindingFlags.Public | BindingFlags.Instance);

		// Track property names to handle `new` hiding:
		// if multiple properties have the same name, keep the one from
		// the most derived type (greatest inheritance depth).
		var bestByName = new Dictionary<string, (PropertyInfo Prop, int Depth)>();

		foreach (var prop in properties)
		{
			if (prop.DeclaringType == typeof(object))
				continue;
			// Include properties with public setter OR collection-type
			// properties with public getter (XAML adds items via getter)
			if (!HasPublicSetter(prop) && !IsReadOnlyCollectionProperty(prop))
				continue;
			if (HasAttribute(prop, "ObsoleteAttribute"))
				continue;
			if (IsBrowsableFalse(prop))
				continue;

			int depth = GetInheritanceDepth(prop.DeclaringType!);
			if (bestByName.TryGetValue(prop.Name, out var existing))
			{
				if (depth > existing.Depth)
					bestByName[prop.Name] = (prop, depth);
			}
			else
			{
				bestByName[prop.Name] = (prop, depth);
			}
		}

		foreach (var (name, (prop, _)) in bestByName)
		{
			// Skip internal/infrastructure properties from XamlElement
			if (IsInfrastructureProperty(name))
				continue;

			var propType = prop.PropertyType;
			var underlyingType = Nullable.GetUnderlyingType(propType);
			var effectiveType = underlyingType ?? propType;

			var schema = new PropertySchema
			{
				DeclaredType = FormatTypeName(propType)
			};

			if (effectiveType.IsEnum)
			{
				schema.IsEnum = true;
				CollectEnum(effectiveType);
			}

			if (_xamlElementType.IsAssignableFrom(effectiveType))
				schema.IsElement = true;

			bool isCollection = IsCollectionType(effectiveType);
			if (isCollection)
			{
				schema.IsCollection = true;
				string? itemType = GetCollectionItemType(effectiveType);
				if (itemType != null)
					schema.CollectionItemType = itemType;
			}

			schema.CanBeAttribute = DetermineCanBeAttribute(
				effectiveType, schema.IsElement, isCollection);

			string? propDescription = GetXmlDocSummary(prop);
			if (propDescription != null)
				schema.Description = propDescription;

			result[name] = schema;
		}

		return result;
	}

	#endregion

	#region Enum Collection

	private void CollectEnum(Type enumType)
	{
		string fullName = FormatTypeName(enumType);
		if (_enums.ContainsKey(fullName))
			return;

		var names = Enum.GetNames(enumType);
		var values = Enum.GetValues(enumType);
		var seen = new Dictionary<long, string>();
		var result = new List<string>();

		for (int i = 0; i < names.Length; i++)
		{
			long numericValue = Convert.ToInt64(values.GetValue(i));
			if (seen.TryGetValue(numericValue, out string? original))
			{
				Console.WriteLine(
					$"  Warning: enum alias '{enumType.Name}.{names[i]}'" +
					$" == '{original}', skipping.");
				continue;
			}
			seen[numericValue] = names[i];
			result.Add(names[i]);
		}

		_enums[fullName] = result;
	}

	#endregion

	#region AllowedChildElements

	private void BuildAllowedChildElements()
	{
		// Collect all unique "required types" from element/collection properties
		var requiredTypes = new HashSet<string>();

		foreach (var element in _elements.Values)
		{
			foreach (var prop in element.Properties.Values)
			{
				if (prop.IsElement == true)
					requiredTypes.Add(prop.DeclaredType);
				if (prop.IsCollection == true && prop.CollectionItemType != null)
					requiredTypes.Add(prop.CollectionItemType);
			}
		}

		foreach (string requiredTypeName in requiredTypes)
		{
			var requiredType = ResolveType(requiredTypeName);
			if (requiredType == null)
				continue;

			var compatible = new List<string>();
			foreach (var (tagName, elementType) in _elementTypesByTag)
			{
				if (requiredType.IsAssignableFrom(elementType))
					compatible.Add(tagName);
			}

			if (compatible.Count > 0)
			{
				compatible.Sort(StringComparer.Ordinal);
				_allowedChildElements[requiredTypeName] = compatible;
			}
		}

		Console.WriteLine(
			$"Built {_allowedChildElements.Count} allowedChildElements entries.");
	}

	#endregion

	#region Attached Properties

	private void DiscoverAttachedProperties(Type[] allTypes)
	{
		var uiElementBase = FindType("UIElementBase");

		// Pre-compute applicable tags (all UIElementBase descendants)
		List<string>? cachedApplicableTags = null;
		if (uiElementBase != null)
		{
			cachedApplicableTags = _elementTypesByTag
				.Where(kv => uiElementBase.IsAssignableFrom(kv.Value))
				.Select(kv => kv.Key)
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToList();
		}

		foreach (var type in allTypes)
		{
			if (!_xamlElementType.IsAssignableFrom(type))
				continue;
			if (!type.IsPublic)
				continue;

			object? attachedAttr = null;
			try
			{
				attachedAttr = type.GetCustomAttributes(true)
					.FirstOrDefault(a => a.GetType().Name == "AttachedPropertiesAttribute");
			}
			catch (Exception)
			{
				continue;
			}
			if (attachedAttr == null)
				continue;

			string? propsValue = null;
			// Try known property names: "Properties", "List"
			foreach (string propNameCandidate in new[] { "Properties", "List" })
			{
				var pi = attachedAttr.GetType().GetProperty(propNameCandidate);
				if (pi != null)
				{
					propsValue = pi.GetValue(attachedAttr) as string;
					if (!string.IsNullOrEmpty(propsValue))
						break;
				}
			}
			if (string.IsNullOrEmpty(propsValue))
				continue;

			string ownerName = type.Name;
			string[] propNames = propsValue.Split(',',
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			foreach (string propName in propNames)
			{
				string key = $"{ownerName}.{propName}";
				Type propType = ResolveAttachedPropertyType(type, propName);

				var underlyingType = Nullable.GetUnderlyingType(propType);
				var effectiveType = underlyingType ?? propType;

				var schema = new AttachedPropertySchema
				{
					Type = FormatTypeName(propType),
					CanBeAttribute = DetermineCanBeAttribute(
						effectiveType, isElement: false, isCollection: false)
				};

				if (effectiveType.IsEnum)
				{
					schema.IsEnum = true;
					CollectEnum(effectiveType);
				}

				schema.ApplicableTags = cachedApplicableTags;

				_attachedProperties[key] = schema;
			}
		}

		Console.WriteLine(
			$"Discovered {_attachedProperties.Count} attached properties.");
	}

	#endregion

	#region Type Helpers

	private Type? FindType(string shortName)
	{
		return _assembly.GetExportedTypes()
			.FirstOrDefault(t => t.Name == shortName);
	}

	private Type? ResolveType(string formattedName)
	{
		// Strip trailing '?' for Nullable types
		string lookupName = formattedName.EndsWith('?')
			? formattedName[..^1]
			: formattedName;

		// Fast path: lookup in pre-built index
		if (_typeByFormattedName.TryGetValue(lookupName, out var type))
			return type;

		// Try by CLR name directly (for System types etc.)
		type = _assembly.GetType(lookupName);
		if (type != null)
			return type;

		// Try referenced assemblies
		foreach (var refName in _assembly.GetReferencedAssemblies())
		{
			try
			{
				var refAssembly = Assembly.Load(refName);
				type = refAssembly.GetType(lookupName);
				if (type != null)
					return type;
			}
			catch
			{
				// Ignore load failures for optional references
			}
		}

		// Last resort: all loaded assemblies
		return AppDomain.CurrentDomain.GetAssemblies()
			.Select(a => a.GetType(lookupName))
			.FirstOrDefault(t => t != null);
	}

	private static Type ResolveAttachedPropertyType(Type ownerType, string propName)
	{
		// Try exact Get{PropName} first
		var getMethod = ownerType.GetMethod($"Get{propName}",
			BindingFlags.Public | BindingFlags.Instance);
		if (getMethod != null)
			return getMethod.ReturnType;

		// Try exact Set{PropName}
		var setMethod = ownerType.GetMethod($"Set{propName}",
			BindingFlags.Public | BindingFlags.Instance);
		if (setMethod != null)
		{
			var parameters = setMethod.GetParameters();
			if (parameters.Length == 2)
				return parameters[1].ParameterType;
		}

		// Try exact property with matching name
		var prop = ownerType.GetProperty(propName,
			BindingFlags.Public | BindingFlags.Instance);
		if (prop != null)
			return prop.PropertyType;

		return typeof(object);
	}

	private static string GetSubNamespacePrefix(Type type, string rootNs)
	{
		string ns = type.Namespace ?? "";
		if (ns.StartsWith(rootNs + "."))
		{
			string sub = ns.Substring(rootNs.Length + 1);
			return sub + ".";
		}
		return ns + ".";
	}

	private static string GetTagName(Type type)
	{
		// Check [XamlName("...")] attribute
		var xamlNameAttr = type.GetCustomAttributes(false)
			.FirstOrDefault(a => a.GetType().Name == "XamlNameAttribute");
		if (xamlNameAttr != null)
		{
			var nameProp = xamlNameAttr.GetType().GetProperty("Name");
			if (nameProp != null)
			{
				string? name = nameProp.GetValue(xamlNameAttr) as string;
				if (!string.IsNullOrEmpty(name))
					return name;
			}
		}

		return type.Name;
	}

	private static string? GetContentPropertyName(Type type)
	{
		// Walk up the hierarchy to find [ContentProperty("...")]
		var current = type;
		while (current != null && current != typeof(object))
		{
			var attr = current.GetCustomAttributes(false)
				.FirstOrDefault(a => a.GetType().Name == "ContentPropertyAttribute");
			if (attr != null)
			{
				var nameProp = attr.GetType().GetProperty("Name");
				if (nameProp != null)
					return nameProp.GetValue(attr) as string;
			}
			current = current.BaseType;
		}
		return null;
	}

	private static bool HasAttribute(MemberInfo member, string attributeTypeName)
	{
		return member.GetCustomAttributes(false)
			.Any(a => a.GetType().Name == attributeTypeName);
	}

	private static bool HasAttributeInHierarchy(Type type, string attributeTypeName)
	{
		var current = type;
		while (current != null && current != typeof(object))
		{
			if (HasAttribute(current, attributeTypeName))
				return true;
			current = current.BaseType;
		}
		return false;
	}

	private static bool IsBrowsableFalse(MemberInfo member)
	{
		var browsable = member.GetCustomAttributes(false)
			.FirstOrDefault(a => a is BrowsableAttribute) as BrowsableAttribute;
		return browsable is { Browsable: false };
	}

	private static int GetInheritanceDepth(Type type)
	{
		int depth = 0;
		var current = type;
		while (current != null)
		{
			depth++;
			current = current.BaseType;
		}
		return depth;
	}

	private static string FormatTypeName(Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type);
		if (underlying != null)
			return FormatTypeName(underlying) + "?";

		// Nested types: replace '+' with '.'
		string? fullName = type.FullName;
		if (fullName != null)
			return fullName.Replace('+', '.');

		return type.Name;
	}

	private static bool IsInfrastructureProperty(string name)
	{
		return name is "Bindings" or "Attach" or "BindImpl"
			or "BaseUri" or "XamlStyle";
	}

	private static bool IsReadOnlyCollectionProperty(PropertyInfo prop)
	{
		var getter = prop.GetGetMethod();
		if (getter == null || !getter.IsPublic)
			return false;
		return IsCollectionType(prop.PropertyType);
	}

	private static bool HasPublicSetter(PropertyInfo prop)
	{
		var setter = prop.GetSetMethod();
		if (setter != null && setter.IsPublic)
			return true;

		// init-only accessors in .NET 5+ have IsInitOnly on return parameter
		// but GetSetMethod() returns them as well; however,
		// init-only properties still have a set method accessible via reflection.
		// We exclude known init-only infrastructure props via IsInfrastructureProperty.
		return false;
	}

	private static bool IsCollectionType(Type type)
	{
		if (type.IsArray)
			return true;

		if (type == typeof(string))
			return false;

		return type.GetInterfaces()
			.Any(i => i.IsGenericType
				&& (i.GetGenericTypeDefinition() == typeof(IList<>)
					|| i.GetGenericTypeDefinition() == typeof(ICollection<>)
					|| i.GetGenericTypeDefinition() == typeof(IEnumerable<>)));
	}

	private static string? GetCollectionItemType(Type type)
	{
		// 1. Array
		if (type.IsArray)
		{
			var elementType = type.GetElementType();
			return elementType != null ? FormatTypeName(elementType) : null;
		}

		// 2. Known non-generic collections (use FullName with '+' replaced)
		string typeName = FormatTypeName(type);
		if (KnownCollectionItemTypes.TryGetValue(typeName, out string? known))
			return known;

		// 3. Generic IList<T> / ICollection<T> / IEnumerable<T>
		var genericInterfaces = type.GetInterfaces()
			.Where(i => i.IsGenericType)
			.ToList();

		Type? itemInterface =
			genericInterfaces.FirstOrDefault(
				i => i.GetGenericTypeDefinition() == typeof(IList<>))
			?? genericInterfaces.FirstOrDefault(
				i => i.GetGenericTypeDefinition() == typeof(ICollection<>))
			?? genericInterfaces.FirstOrDefault(
				i => i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

		if (itemInterface != null)
		{
			var itemType = itemInterface.GetGenericArguments()[0];
			return FormatTypeName(itemType);
		}

		// 4. Check base type if it is generic (List<T> etc.)
		var baseType = type.BaseType;
		while (baseType != null && baseType != typeof(object))
		{
			if (baseType.IsGenericType)
			{
				var args = baseType.GetGenericArguments();
				if (args.Length == 1)
					return FormatTypeName(args[0]);
			}
			baseType = baseType.BaseType;
		}

		return null;
	}

	private bool DetermineCanBeAttribute(
		Type effectiveType, bool? isElement, bool isCollection)
	{
		if (isElement == true)
			return false;

		if (effectiveType == typeof(object))
			return false;

		if (effectiveType == typeof(string))
			return true;

		if (effectiveType == typeof(bool))
			return true;

		if (effectiveType.IsPrimitive || effectiveType == typeof(decimal))
			return true;

		if (effectiveType.IsEnum)
			return true;

		if (effectiveType == typeof(Guid)
			|| effectiveType == typeof(DateTime)
			|| effectiveType == typeof(TimeSpan))
			return true;

		// Collections of XamlElement descendants are never attributes
		if (isCollection)
		{
			string? itemTypeName = GetCollectionItemType(effectiveType);
			if (itemTypeName != null)
			{
				var itemType = ResolveType(itemTypeName);
				if (itemType != null
					&& _xamlElementType.IsAssignableFrom(itemType))
					return false;
			}
		}

		// Types with TypeConverter from string (e.g. RowDefinitions, Length, etc.)
		try
		{
			var converter = TypeDescriptor.GetConverter(effectiveType);
			if (converter.CanConvertFrom(typeof(string)))
				return true;
		}
		catch
		{
			// TypeDescriptor may fail for some types in reflection-only context
		}

		if (isCollection)
			return false;

		return false;
	}

	#endregion

	#region XML Documentation

	private static XPathNavigator? LoadXmlDoc(Assembly assembly)
	{
		string? assemblyPath = assembly.Location;
		if (string.IsNullOrEmpty(assemblyPath))
			return null;

		string xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
		if (!File.Exists(xmlPath))
		{
			Console.WriteLine($"XML doc not found: {xmlPath}");
			return null;
		}

		try
		{
			Console.WriteLine($"Loading XML doc: {xmlPath}");
			var doc = new XmlDocument();
			doc.Load(xmlPath);
			return doc.CreateNavigator();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Warning: failed to load XML doc: {ex.Message}");
			return null;
		}
	}

	private string? GetXmlDocSummary(Type type)
	{
		if (_xmlDoc == null)
			return null;

		string memberName = $"T:{type.FullName}";
		return ExtractSummary(memberName);
	}

	private string? GetXmlDocSummary(PropertyInfo property)
	{
		if (_xmlDoc == null)
			return null;

		string memberName = $"P:{property.DeclaringType?.FullName}.{property.Name}";
		return ExtractSummary(memberName);
	}

	private string? ExtractSummary(string memberName)
	{
		var node = _xmlDoc?.SelectSingleNode(
			$"/doc/members/member[@name='{memberName}']/summary");
		if (node == null)
			return null;

		string text = node.InnerXml.Trim();
		// Collapse whitespace
		text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
		return string.IsNullOrEmpty(text) ? null : text;
	}

	#endregion
}

#region Schema Models

internal sealed class ElementSchema
{
	public string ElementClrType { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? ContentProperty { get; set; }
	public bool? ContentAsXamlAttr { get; set; }
	public Dictionary<string, PropertySchema> Properties { get; set; } = new();
}

internal sealed class PropertySchema
{
	public string DeclaredType { get; set; } = string.Empty;
	public bool? IsEnum { get; set; }
	public bool? IsElement { get; set; }
	public bool? IsCollection { get; set; }
	public string? CollectionItemType { get; set; }
	public bool CanBeAttribute { get; set; }
	public string? Description { get; set; }
}

internal sealed class AttachedPropertySchema
{
	public string Type { get; set; } = string.Empty;
	public bool? IsEnum { get; set; }
	public bool CanBeAttribute { get; set; }
	public List<string>? ApplicableTags { get; set; }
}

#endregion
