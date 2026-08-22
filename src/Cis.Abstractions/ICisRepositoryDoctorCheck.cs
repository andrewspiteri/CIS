namespace Cis.Abstractions;

public interface ICisRepositoryDoctorCheck
{
    string Name { get; }

    IReadOnlyList<CisRepositoryDoctorFinding> Inspect(CisRepositoryContext context);
}
