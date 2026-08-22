using System.CommandLine;

namespace Cis.Abstractions;

public interface ICisCommandRegistry
{
    void Add(Command command);
}
