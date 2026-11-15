using System.Reflection;

var install = args.Length > 0 ? args[0] : @"C:\Program Files\paint.net";
var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
var paths = Directory.GetFiles(install, "*.dll")
    .Concat(Directory.GetFiles(runtimeDirectory, "*.dll"))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

using var loadContext = new MetadataLoadContext(new PathAssemblyResolver(paths));
var assemblyPaths = new[] { "paintdotnet.dll", "PaintDotNet.Data.dll", "PaintDotNet.Core.dll", "PaintDotNet.FileTypes.dll", "PaintDotNet.Framework.dll" }
    .Select(name => Path.Combine(install, name))
    .Where(File.Exists)
    .ToArray();
var assemblies = assemblyPaths.Select(loadContext.LoadFromAssemblyPath).ToArray();

foreach (var type in assemblies.SelectMany(a => a.GetTypes())
    .Where(type => type.FullName is not null &&
        (type.FullName == "PaintDotNet.HistoryStack" ||
         type.FullName.Contains("PaintDotNet.Document", StringComparison.OrdinalIgnoreCase) ||
         type.FullName.Contains("PaintDotNet.Layer", StringComparison.OrdinalIgnoreCase) ||
         type.FullName.Contains("History", StringComparison.OrdinalIgnoreCase) ||
         type.FullName.Contains("DocumentWorkspace", StringComparison.OrdinalIgnoreCase) ||
         type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
             .Any(property => property.Name.Contains("ActiveDocumentWorkspace", StringComparison.OrdinalIgnoreCase))))
    .OrderBy(type => type.FullName))
{
    var events = type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Where(e => e.Name.Contains("History", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.Contains("Document", StringComparison.OrdinalIgnoreCase))
        .Select(e => $"    event {e.Attributes} {e.EventHandlerType?.FullName} {e.Name}")
        .ToArray();
    var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Where(p => type.FullName == "PaintDotNet.HistoryStack" ||
                    p.Name.Contains("History", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Document", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Active", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("File", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Name", StringComparison.OrdinalIgnoreCase))
        .Select(p => $"    property {p.PropertyType.FullName} {p.Name}")
        .ToArray();
    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m => type.FullName == "PaintDotNet.HistoryStack" ||
                    m.Name.Contains("History", StringComparison.OrdinalIgnoreCase) ||
                    m.Name.Contains("Document", StringComparison.OrdinalIgnoreCase))
        .Select(m => $"    method {m.Attributes} {m}")
        .ToArray();

    if (events.Length == 0 && properties.Length == 0 && methods.Length == 0)
        continue;

    Console.WriteLine($"{type.Attributes} {type.FullName}");
    foreach (var item in events.Concat(properties).Concat(methods))
        Console.WriteLine(item);
}
