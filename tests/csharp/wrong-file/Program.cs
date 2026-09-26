// Prints MeshyFileCheck.DescribeWrongFile for each file argument as a JSON array ("" = no problem).
using System.IO;
using System.Linq;
using System.Text.Json;
using FISHHWB.MeshyImporter.Editor;

internal static class Program
{
    private static void Main(string[] args)
    {
        var messages = args.Select(p => MeshyFileCheck.DescribeWrongFile(File.ReadAllBytes(p)) ?? "").ToArray();
        System.Console.WriteLine(JsonSerializer.Serialize(messages));
    }
}
