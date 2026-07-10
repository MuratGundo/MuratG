namespace UCrew.TTARCH2.Core.Models;

public sealed class HeaderModel
{
    public string Magic { get; set; } = string.Empty;

    public long HeaderLength { get; set; }

    public uint Field0004 { get; set; }

    public uint Field0008 { get; set; }

    public uint Field000C { get; set; }

    public uint Field0010 { get; set; }

    public uint Field0014 { get; set; }

    public uint Field0018 { get; set; }

    public uint Field001C { get; set; }
}
