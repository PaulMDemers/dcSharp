namespace DcSharp.Core.Dreamcast.Input;

public static class DreamcastControllerStateParser
{
    public static DreamcastControllerScript ParseScript(string text)
    {
        var frames = new List<DreamcastControllerScriptFrame>();
        foreach (var rawFrame in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = rawFrame.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidDataException("Controller script frames must use instruction:state syntax.");
            }

            if (!ulong.TryParse(rawFrame[..separator], out var instruction))
            {
                throw new InvalidDataException($"Invalid controller script instruction: {rawFrame[..separator]}");
            }

            frames.Add(new DreamcastControllerScriptFrame(instruction, ParseState(rawFrame[(separator + 1)..])));
        }

        return new DreamcastControllerScript(frames.OrderBy(frame => frame.FromInstruction).ToArray());
    }

    public static DreamcastControllerState ParseState(string text)
    {
        var buttons = DreamcastControllerButtons.None;
        byte leftTrigger = 0;
        byte rightTrigger = 0;
        sbyte joyX = 0;
        sbyte joyY = 0;
        sbyte joy2X = 0;
        sbyte joy2Y = 0;

        foreach (var rawToken in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = rawToken.Trim();
            var equals = token.IndexOf('=');
            if (equals < 0)
            {
                buttons |= ParseButton(token);
                continue;
            }

            var key = token[..equals].Trim().ToLowerInvariant();
            var value = token[(equals + 1)..].Trim();
            switch (key)
            {
                case "buttons":
                    foreach (var button in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        buttons |= ParseButton(button);
                    }
                    break;
                case "ltrig":
                case "lt":
                    leftTrigger = ParseByte(value, key);
                    break;
                case "rtrig":
                case "rt":
                    rightTrigger = ParseByte(value, key);
                    break;
                case "joyx":
                    joyX = ParseAxis(value, key);
                    break;
                case "joyy":
                    joyY = ParseAxis(value, key);
                    break;
                case "joy2x":
                    joy2X = ParseAxis(value, key);
                    break;
                case "joy2y":
                    joy2Y = ParseAxis(value, key);
                    break;
                default:
                    throw new InvalidDataException($"Unknown controller field: {key}");
            }
        }

        return new DreamcastControllerState(buttons, leftTrigger, rightTrigger, joyX, joyY, joy2X, joy2Y);
    }

    private static DreamcastControllerButtons ParseButton(string text)
    {
        var normalized = text.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "a" => DreamcastControllerButtons.A,
            "b" => DreamcastControllerButtons.B,
            "c" => DreamcastControllerButtons.C,
            "d" => DreamcastControllerButtons.D,
            "x" => DreamcastControllerButtons.X,
            "y" => DreamcastControllerButtons.Y,
            "z" => DreamcastControllerButtons.Z,
            "start" => DreamcastControllerButtons.Start,
            "up" or "dpadup" => DreamcastControllerButtons.DPadUp,
            "down" or "dpaddown" => DreamcastControllerButtons.DPadDown,
            "left" or "dpadleft" => DreamcastControllerButtons.DPadLeft,
            "right" or "dpadright" => DreamcastControllerButtons.DPadRight,
            "dpad2up" => DreamcastControllerButtons.DPad2Up,
            "dpad2down" => DreamcastControllerButtons.DPad2Down,
            "dpad2left" => DreamcastControllerButtons.DPad2Left,
            "dpad2right" => DreamcastControllerButtons.DPad2Right,
            "none" => DreamcastControllerButtons.None,
            _ => throw new InvalidDataException($"Unknown controller button: {text}")
        };
    }

    private static byte ParseByte(string text, string key)
    {
        if (!byte.TryParse(text, out var value))
        {
            throw new InvalidDataException($"{key} must be between 0 and 255.");
        }

        return value;
    }

    private static sbyte ParseAxis(string text, string key)
    {
        if (!int.TryParse(text, out var parsed) || parsed is < -128 or > 127)
        {
            throw new InvalidDataException($"{key} must be between -128 and 127.");
        }

        return (sbyte)parsed;
    }
}
