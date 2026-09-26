using Ossapi.Models;

namespace HitCircleAPI.Services;

public record PpResult(double Pp, double Stars, uint MaxCombo);

public interface IPpCalculatorService
{
    PpResult Calculate(Score score, string osuFilePath);
    (double IfPp, double SsPp) CalculateIfFcAndSs(Score score, string osuFilePath);
    PpResult CalculateSs(string osuFilePath, int rulesetId, uint mods = 0);
    (double NewPp, int Position) FindOptimalNewPp(List<double> ppList, double desiredIncrease);
}
