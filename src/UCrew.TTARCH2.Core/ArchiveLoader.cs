using UCrew.TTARCH2.Core.Models;

namespace UCrew.TTARCH2.Core;

public static class ArchiveLoader
{
    public static ArchiveModel Open(string filePath)
    {
        FileInfo info = new(filePath);

        if (!info.Exists)
            throw new FileNotFoundException("Archive file not found.", filePath);

        return new ArchiveModel
        {
            FileName = info.Name,
            FullPath = info.FullName,
            FileSize = info.Length
        };
    }
}
