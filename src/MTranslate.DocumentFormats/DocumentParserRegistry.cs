namespace MTranslate.DocumentFormats;

public sealed class DocumentParserRegistry
{
    private readonly IReadOnlyList<IDocumentParser> parsers;

    public DocumentParserRegistry(IEnumerable<IDocumentParser>? parsers = null)
    {
        this.parsers = (parsers ??
        [
            new TxtDocumentParser(),
            new SrtDocumentParser(),
            new VttDocumentParser(),
            new MarkdownDocumentParser(),
            new AssDocumentParser()
        ]).ToArray();
        if (this.parsers.Select(parser => parser.Format).Distinct().Count() != this.parsers.Count)
            throw new ArgumentException("Only one parser may be registered for each document format.", nameof(parsers));
    }

    public IReadOnlyList<IDocumentParser> Parsers => parsers;

    public IDocumentParser Resolve(string pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension))
            throw new ArgumentException("A document path or extension is required.", nameof(pathOrExtension));
        var extension = pathOrExtension.StartsWith('.') ? pathOrExtension : Path.GetExtension(pathOrExtension);
        return parsers.FirstOrDefault(parser => parser.CanHandle(extension))
            ?? throw new NotSupportedException($"Document extension '{extension}' is not supported.");
    }
}
