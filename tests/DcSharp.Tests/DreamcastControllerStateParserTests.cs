using DcSharp.Core.Dreamcast.Input;

namespace DcSharp.Tests;

public class DreamcastControllerStateParserTests
{
    [Theory]
    [InlineData("a0", 0x20)]
    [InlineData("b0", 0x40)]
    [InlineData("c0", 0x60)]
    [InlineData("d0", 0x80)]
    public void ParseMapleAddressAcceptsControllerPorts(string text, byte expectedAddress)
    {
        Assert.Equal(expectedAddress, DreamcastControllerStateParser.ParseMapleAddress(text));
    }

    [Fact]
    public void ParseMapEntryAcceptsAddressAndState()
    {
        var entry = DreamcastControllerStateParser.ParseMapEntry("b0:b,ltrig=7");

        Assert.Equal(0x40, entry.Key);
        Assert.Equal(DreamcastControllerButtons.B, entry.Value.Buttons);
        Assert.Equal(7, entry.Value.LeftTrigger);
    }

    [Fact]
    public void ParseScriptMapEntryAcceptsAddressAndScript()
    {
        var entry = DreamcastControllerStateParser.ParseScriptMapEntry("b0:0:none;10:b,ltrig=7");

        Assert.Equal(0x40, entry.Key);
        Assert.Equal(DreamcastControllerButtons.None, entry.Value.StateAt(0).Buttons);
        Assert.Equal(DreamcastControllerButtons.B, entry.Value.StateAt(10).Buttons);
        Assert.Equal(7, entry.Value.StateAt(10).LeftTrigger);
    }

    [Fact]
    public void ParseStateAcceptsButtonsTriggersAndAxes()
    {
        var state = DreamcastControllerStateParser.ParseState("start,a,joyx=-12,joyy=13,joy2x=-2,joy2y=3,ltrig=40,rtrig=80");

        Assert.Equal(DreamcastControllerButtons.Start | DreamcastControllerButtons.A, state.Buttons);
        Assert.Equal(-12, state.JoyX);
        Assert.Equal(13, state.JoyY);
        Assert.Equal(-2, state.Joy2X);
        Assert.Equal(3, state.Joy2Y);
        Assert.Equal(40, state.LeftTrigger);
        Assert.Equal(80, state.RightTrigger);
    }

    [Fact]
    public void ParseScriptOrdersFramesByInstruction()
    {
        var script = DreamcastControllerStateParser.ParseScript("20:start;0:none;10:a");

        Assert.Equal(DreamcastControllerButtons.None, script.StateAt(0).Buttons);
        Assert.Equal(DreamcastControllerButtons.None, script.StateAt(9).Buttons);
        Assert.Equal(DreamcastControllerButtons.A, script.StateAt(10).Buttons);
        Assert.Equal(DreamcastControllerButtons.A, script.StateAt(19).Buttons);
        Assert.Equal(DreamcastControllerButtons.Start, script.StateAt(20).Buttons);
    }

    [Fact]
    public void ParseScriptRejectsInvalidFrameSyntax()
    {
        var ex = Assert.Throws<InvalidDataException>(() => DreamcastControllerStateParser.ParseScript("100-start"));

        Assert.Contains("instruction:state", ex.Message);
    }
}
