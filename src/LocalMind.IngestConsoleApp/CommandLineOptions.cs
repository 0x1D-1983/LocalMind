using CommandLine;

namespace LocalMind.IngestConsoleApp;

public class CommandLineOptions
{
    [Option('d', "document", Required = true, HelpText = "Path to the document file to ingest.")]
    public string DocumentPath { get; set; } = string.Empty;
}