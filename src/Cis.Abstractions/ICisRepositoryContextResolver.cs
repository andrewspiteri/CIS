namespace Cis.Abstractions;

public interface ICisRepositoryContextResolver
{
    CisRepositoryContextResolution Resolve(string repositoryPath);
}
