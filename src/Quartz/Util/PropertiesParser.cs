#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using System.Collections.Specialized;
using System.Globalization;

namespace Quartz.Util;

/// <summary>
/// This is an utility class used to parse the properties.
/// </summary>
/// <author> James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class PropertiesParser
{
    internal readonly NameValueCollection props;

    /// <summary>
    /// Gets the underlying properties.
    /// </summary>
    /// <value>The underlying properties.</value>
    public NameValueCollection UnderlyingProperties => props;

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertiesParser"/> class.
    /// </summary>
    /// <param name="props">The props.</param>
    public PropertiesParser(NameValueCollection props)
    {
        this.props = props;
    }

    /// <summary>
    /// Gets the string property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public string? GetStringProperty(string? name)
    {
        var val = props.Get(name);
        return val?.Trim();
    }

    /// <summary>
    /// Gets the string property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public string? GetStringProperty(string name, string? defaultValue)
    {
        string? val = props[name] ?? defaultValue;
        if (val is null)
        {
            return defaultValue;
        }
        val = val.Trim();
        if (val.Length == 0)
        {
            return defaultValue;
        }
        return val;
    }

    /// <summary>
    /// Gets the string array property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public IList<string>? GetStringArrayProperty(string name)
    {
        return GetStringArrayProperty(name, null);
    }

    /// <summary>
    /// Gets the string array property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public IList<string>? GetStringArrayProperty(string name, string[]? defaultValue)
    {
        var vals = GetStringProperty(name);
        if (vals is null)
        {
            return defaultValue;
        }

        string[] items = vals.Split(',');
        List<string> strs = new List<string>(items.Length);
        try
        {
            strs.AddRange(items.Select(s => s.Trim()));

            return strs;
        }
        catch (Exception)
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Gets the boolean property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public bool GetBooleanProperty(string name)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return false;
        }

        return val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the boolean property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">if set to <c>true</c> [defaultValue].</param>
    /// <returns></returns>
    public bool GetBooleanProperty(string name, bool defaultValue)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return defaultValue;
        }

        return val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the byte property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public byte GetByteProperty(string name)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            ThrowHelper.ThrowFormatException(" null string");
        }

        try
        {
            return byte.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the byte property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public byte GetByteProperty(string name, byte defaultValue)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return defaultValue;
        }

        try
        {
            return byte.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the char property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public char GetCharProperty(string name)
    {
        var param = GetStringProperty(name);
        if (param is null)
        {
            return '\x0000';
        }

        if (param.Length == 0)
        {
            return '\x0000';
        }

        return param[0];
    }

    /// <summary>
    /// Gets the char property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public char GetCharProperty(string name, char defaultValue)
    {
        var param = GetStringProperty(name);
        if (param is null)
        {
            return defaultValue;
        }

        if (param.Length == 0)
        {
            return defaultValue;
        }

        return param[0];
    }

    /// <summary>
    /// Gets the double property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public double GetDoubleProperty(string name)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            ThrowHelper.ThrowFormatException(" null string");
        }

        try
        {
            return double.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the double property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public double GetDoubleProperty(string name, double defaultValue)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return defaultValue;
        }

        try
        {
            return double.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the float property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public float GetFloatProperty(string name)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            ThrowHelper.ThrowFormatException(" null string");
        }

        try
        {
            return float.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the float property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public float GetFloatProperty(string name, float defaultValue)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return defaultValue;
        }

        try
        {
            return float.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the int property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public int GetIntProperty(string name)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            ThrowHelper.ThrowFormatException(" null string");
        }

        try
        {
            return int.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the int property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public int GetIntProperty(string name, int defaultValue)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return defaultValue;
        }

        try
        {
            return int.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the int array property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public IList<int>? GetIntArrayProperty(string name)
    {
        return GetIntArrayProperty(name, null);
    }

    /// <summary>
    /// Gets the int array property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public IList<int>? GetIntArrayProperty(string name, IList<int>? defaultValue)
    {
        var vals = GetStringProperty(name);
        if (vals is null)
        {
            return defaultValue;
        }

        if (!string.IsNullOrEmpty(vals))
        {
            string[] stok = vals.Split(',');
            List<int> ints = new List<int>();
            try
            {
                foreach (string s in stok)
                {
                    try
                    {
                        ints.Add(int.Parse(s, CultureInfo.InvariantCulture));
                    }
                    catch (FormatException)
                    {
                        ThrowHelper.ThrowFormatException($" '{vals}'");
                    }
                }
                return ints;
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets the long property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public long GetLongProperty(string name)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            ThrowHelper.ThrowFormatException(" null string");
        }

        try
        {
            return long.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the long property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="def">The def.</param>
    /// <returns></returns>
    public long GetLongProperty(string name, long def)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return def;
        }

        try
        {
            return long.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the TimeSpan property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="def">The def.</param>
    /// <returns></returns>
    public TimeSpan GetTimeSpanProperty(string name, TimeSpan def)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return def;
        }

        try
        {
            return TimeSpan.FromMilliseconds(long.Parse(val, CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the short property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public short GetShortProperty(string name)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            ThrowHelper.ThrowFormatException(" null string");
        }

        try
        {
            return short.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the short property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns></returns>
    public short GetShortProperty(string name, short defaultValue)
    {
        var val = GetStringProperty(name);
        if (val is null)
        {
            return defaultValue;
        }

        try
        {
            return short.Parse(val, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            ThrowHelper.ThrowFormatException($" '{val}'");
            return default;
        }
    }

    /// <summary>
    /// Gets the property groups.
    /// </summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns></returns>
    public IReadOnlyList<string> GetPropertyGroups(string prefix)
    {
        var groups = new HashSet<string>();

        if (!prefix.EndsWith('.'))
        {
            prefix += ".";
        }

        foreach (string? key in props.Keys)
        {
            if (key is not null && key.StartsWith(prefix))
            {
                string groupName = key.Substring(prefix.Length, key.IndexOf('.', prefix.Length) - prefix.Length);
                groups.Add(groupName);
            }
        }

        return new List<string>(groups);
    }

    /// <summary>
    /// Gets the property group.
    /// </summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns></returns>
    public NameValueCollection GetPropertyGroup(string prefix)
    {
        return GetPropertyGroup(prefix, false);
    }

    /// <summary>
    /// Gets the property group.
    /// </summary>
    /// <param name="prefix">The prefix.</param>
    /// <param name="stripPrefix">if set to <c>true</c> [strip prefix].</param>
    /// <returns></returns>
    public NameValueCollection GetPropertyGroup(string prefix, bool stripPrefix)
    {
        return GetPropertyGroup(prefix, stripPrefix, null);
    }


    /// <summary>
    /// Get all properties that start with the given prefix.
    /// </summary>
    /// <param name="prefix">The prefix for which to search.  If it does not end in a "." then one will be added to it for search purposes.</param>
    /// <param name="stripPrefix">Whether to strip off the given <paramref name="prefix" /> in the result's keys.</param>
    /// <param name="excludedPrefixes">Optional array of fully qualified prefixes to exclude.  For example if <see paramref="prefix" /> is "a.b.c", then <see paramref="excludedPrefixes" /> might be "a.b.c.ignore".</param>
    /// <returns>Group of <see cref="NameValueCollection" /> that start with the given prefix, optionally have that prefix removed, and do not include properties that start with one of the given excluded prefixes.</returns>
    public NameValueCollection GetPropertyGroup(string prefix, bool stripPrefix, string[]? excludedPrefixes)
    {
        NameValueCollection group = new NameValueCollection();

        if (!prefix.EndsWith('.'))
        {
            prefix += ".";
        }

        foreach (string? key in props.Keys)
        {
            if (key is not null && key.StartsWith(prefix))
            {
                bool exclude = false;
                if (excludedPrefixes is not null)
                {
                    for (int i = 0; i < excludedPrefixes.Length && exclude == false; i++)
                    {
                        exclude = key.StartsWith(excludedPrefixes[i]);
                    }
                }

                if (exclude == false)
                {
                    var value = GetStringProperty(key, "");
                    if (stripPrefix)
                    {
                        group[key.Substring(prefix.Length)] = value;
                    }
                    else
                    {
                        group[key] = value;
                    }
                }
            }
        }

        return group;
    }


    /// <summary>
    /// Reads the properties from assembly (embedded resource).
    /// </summary>
    /// <param name="resourceName">The file name to read resources from.</param>
    /// <returns></returns>
    public static PropertiesParser ReadFromEmbeddedAssemblyResource(string resourceName)
    {
        var stream = typeof(IScheduler).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            ThrowHelper.ThrowArgumentException("cannot find resource " + resourceName);
        }
        return ReadFromStream(stream);
    }

    /// <summary>
    /// Reads the properties from file system.
    /// </summary>
    /// <param name="fileName">The file name to read resources from.</param>
    /// <returns></returns>
    public static PropertiesParser ReadFromFileResource(string fileName)
    {
        return ReadFromStream(File.OpenRead(fileName));
    }

    private static PropertiesParser ReadFromStream(Stream stream)
    {
        NameValueCollection props = new NameValueCollection();
        using (StreamReader sr = new StreamReader(stream))
        {
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                line = line.TrimStart();

                if (line.StartsWith('#'))
                {
                    // comment line
                    continue;
                }
                if (line.StartsWith("!END"))
                {
                    // special end condition
                    break;
                }
                string[] lineItems = line.Split('=', 2);
                if (lineItems.Length == 2)
                {
                    props[lineItems[0].Trim()] = lineItems[1].Trim();
                }
            }
        }
        return new PropertiesParser(props);
    }
}