using System.CommandLine;

namespace ConfluenceRag.Commands;

public interface IRagCommand
{
    Command CreateCommand();
}
