using System.Reflection;

var install = args.Length > 0 ? args[0] : @"C:\Program Files\paint.net";
var names = args.Length > 1
    ? args.Skip(1).ToArray()
    : new[] { "paintdotnet.dll", "PaintDotNet.Core.dll", "PaintDotNet.FileTypes.dll", "PaintDotNet.Effects.Core.dll" };

foreach (var name in names)
{
    var path = Path.Combine(install, name);
    Console.WriteLine($"=== {path} ===");
    var asm = Assembly.LoadFrom(path);
    Console.WriteLine($"Assembly: {asm.FullName}");

    foreach (var type in asm.GetTypes().Where(t =>
        t.FullName is not null &&
        (t.FullName.Contains("Pdn", StringComparison.OrdinalIgnoreCase) ||
         t.FullName.Contains("Document", StringComparison.OrdinalIgnoreCase) ||
         t.FullName.Contains("FileType", StringComparison.OrdinalIgnoreCase) ||
         t.FullName.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
         t.FullName.Contains("History", StringComparison.OrdinalIgnoreCase))))
    {
        var members = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Serialize", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Document", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Pdn", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("History", StringComparison.OrdinalIgnoreCase))
            .Select(m => $"    {m.Attributes} {m}")
            .ToArray();
        if (members.Length > 0)
        {
            Console.WriteLine(type.FullName);
            foreach (var member in members) Console.WriteLine(member);
        }
    }
}
