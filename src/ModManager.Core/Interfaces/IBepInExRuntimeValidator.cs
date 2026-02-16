using ModManager.Core.Models;

namespace ModManager.Core.Interfaces;

public interface IBepInExRuntimeValidator
{
    BepInExRuntimeResult Validate(string gamePath);
}
