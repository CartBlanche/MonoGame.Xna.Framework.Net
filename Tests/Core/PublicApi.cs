using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.Xna.Framework.Net.Tests
{
    /// <summary>
    /// A text listing of an assembly's public API (public and protected members of public types), kept in the
    /// repository as a baseline so an unintended change to the API shows up as a failing test. Linked into the
    /// Steam and EOS test projects too.
    /// </summary>
    internal static class PublicApi
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static string Dump(Assembly assembly, string namespacePrefix)
        {
            var lines = new List<string>();
            foreach (Type type in assembly.GetExportedTypes().Where(t => t.Namespace != null && t.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal)).OrderBy(t => t.FullName, StringComparer.Ordinal))
                DescribeType(type, lines);
            return string.Join("\n", lines) + "\n";
        }

        static void DescribeType(Type type, List<string> lines)
        {
            var head = new StringBuilder();
            if (type.IsEnum && type.GetCustomAttribute<FlagsAttribute>() != null) head.Append("[Flags] ");
            if (type.GetCustomAttribute<ObsoleteAttribute>() != null) head.Append("[Obsolete] ");
            head.Append(type.IsEnum ? "enum " : type.IsInterface ? "interface " : type.IsValueType ? "struct " : type.IsAbstract && type.IsSealed ? "static class " : type.IsAbstract ? "abstract class " : type.IsSealed ? "sealed class " : "class ");
            head.Append(Name(type));
            var bases = new List<string>();
            if (!type.IsEnum && !type.IsValueType && !type.IsInterface && type.BaseType != null && type.BaseType != typeof(object)) bases.Add(Name(type.BaseType));
            bases.AddRange(type.GetInterfaces().Where(i => type.BaseType == null || !type.BaseType.GetInterfaces().Contains(i)).Select(Name).OrderBy(n => n, StringComparer.Ordinal));
            if (bases.Count > 0) head.Append(" : ").Append(string.Join(", ", bases));
            lines.Add(head.ToString());

            var members = new List<string>();
            foreach (MemberInfo member in type.GetMembers(All))
            {
                if (member.GetCustomAttribute<CompilerGeneratedAttribute>() != null) continue;
                switch (member)
                {
                    case FieldInfo f when f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly:
                        if (type.IsEnum && f.IsSpecialName) break;
                        members.Add(type.IsEnum ? $"{f.Name} = {Convert.ToInt64(f.GetRawConstantValue())}"
                            : $"{Modifiers(f.IsStatic, f.IsLiteral ? "const" : f.IsInitOnly ? "readonly" : null, f.IsFamily || f.IsFamilyOrAssembly)}field {f.Name} : {Name(f.FieldType)}{(f.IsLiteral ? " = " + Convert.ToString(f.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture) : "")}");
                        break;
                    case ConstructorInfo c when c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly:
                        members.Add($"{Modifiers(false, null, !c.IsPublic)}ctor({Parameters(c)})");
                        break;
                    case MethodInfo m when (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly) && (!m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal)):
                        members.Add($"{Modifiers(m.IsStatic, m.IsAbstract ? "abstract" : m.IsVirtual && !m.IsFinal ? "virtual" : null, !m.IsPublic)}{m.Name}{Generic(m)}({Parameters(m)}) : {Name(m.ReturnType)}");
                        break;
                    case PropertyInfo p:
                    {
                        MethodInfo? g = p.GetMethod, s = p.SetMethod;
                        bool gv = g != null && (g.IsPublic || g.IsFamily || g.IsFamilyOrAssembly), sv = s != null && (s.IsPublic || s.IsFamily || s.IsFamilyOrAssembly);
                        if (!gv && !sv) break;
                        var acc = new List<string>();
                        if (gv) acc.Add(g!.IsPublic ? "get" : "protected get");
                        if (sv) acc.Add(s!.IsPublic ? "set" : "protected set");
                        MethodInfo any = (gv ? g : s)!;
                        members.Add($"{Modifiers(any.IsStatic, any.IsAbstract ? "abstract" : any.IsVirtual && !any.IsFinal ? "virtual" : null, !(gv ? g! : s!).IsPublic)}prop {p.Name}{(p.GetIndexParameters().Length > 0 ? "[" + string.Join(", ", p.GetIndexParameters().Select(x => Name(x.ParameterType))) + "]" : "")} : {Name(p.PropertyType)} {{ {string.Join("; ", acc)} }}");
                        break;
                    }
                    case EventInfo e:
                    {
                        MethodInfo? add = e.AddMethod;
                        if (add == null || !(add.IsPublic || add.IsFamily || add.IsFamilyOrAssembly)) break;
                        members.Add($"{Modifiers(add.IsStatic, null, !add.IsPublic)}event {e.Name} : {Name(e.EventHandlerType!)}");
                        break;
                    }
                }
            }
            foreach (string m in members.OrderBy(m => m, StringComparer.Ordinal))
                lines.Add("    " + m);
        }

        static string Modifiers(bool isStatic, string? other, bool isProtected) =>
            (isProtected ? "protected " : "") + (isStatic ? "static " : "") + (other != null ? other + " " : "");

        static string Parameters(MethodBase m) =>
            string.Join(", ", m.GetParameters().Select(p => (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + Name(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : "")));

        static string Generic(MethodInfo m) => m.IsGenericMethod ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";

        static string Name(Type t)
        {
            if (t.IsByRef) return Name(t.GetElementType()!);
            if (t.IsArray) return Name(t.GetElementType()!) + "[]";
            if (t.IsGenericParameter) return t.Name;
            if (Nullable.GetUnderlyingType(t) is Type u) return Name(u) + "?";
            string baseName = t.IsNested ? Name(t.DeclaringType!) + "." + t.Name : t.Namespace + "." + t.Name;
            int tick = baseName.IndexOf('`');
            if (tick >= 0) baseName = baseName.Substring(0, tick);
            if (t.IsGenericType) baseName += "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">";
            return baseName.Replace("System.", "", StringComparison.Ordinal).Replace("Microsoft.Xna.Framework.", "", StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares the assembly's API with the baseline kept next to the tests. Returns null if they match, otherwise
        /// the differences. Set UPDATE_API_BASELINE=1 to write the current API as the new baseline.
        /// </summary>
        public static string? Compare(Assembly assembly, string namespacePrefix, string baselineFileName, [CallerFilePath] string callerFile = "")
        {
            string baselinePath = Path.Combine(Path.GetDirectoryName(callerFile)!, "PublicApiBaselines", baselineFileName);
            string actual = Dump(assembly, namespacePrefix);

            if (Environment.GetEnvironmentVariable("UPDATE_API_BASELINE") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
                File.WriteAllText(baselinePath, actual);
                return null;
            }

            if (!File.Exists(baselinePath))
                return $"there is no baseline yet at {baselinePath}; run the tests with UPDATE_API_BASELINE=1 to create it";

            string expected = File.ReadAllText(baselinePath).Replace("\r\n", "\n");
            if (expected == actual)
                return null;

            var expectedLines = new HashSet<string>(expected.Split('\n'));
            var actualLines = new HashSet<string>(actual.Split('\n'));
            var diff = new StringBuilder();
            foreach (string l in expected.Split('\n').Where(l => !actualLines.Contains(l) && l.Length > 0)) diff.AppendLine("  removed: " + l.Trim());
            foreach (string l in actual.Split('\n').Where(l => !expectedLines.Contains(l) && l.Length > 0)) diff.AppendLine("  added:   " + l.Trim());
            return diff.ToString();
        }
    }
}
