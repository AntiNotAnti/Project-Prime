using System;
using MphRead.Droid;
using MphRead.Mods.Input;

internal static class StylusResponseChecks
{
    public static void Run()
    {
        var response = new StylusAimResponse();
        if (response.Sample(TouchStylusResponse.Direct, true, 3, -2) != (3f,-2f)
            || response.Sample(TouchStylusResponse.Direct, false, 7, -4) != (7f,-4f)
            || response.Sample(TouchStylusResponse.Direct, false, 0, 0) != (0f,0f))
            throw new Exception("Direct loses short/released flick or repeats it");
        for (int step = 1; step <= 10; step++)
            if (response.Sample(TouchStylusResponse.Classic, true, 8, -4) != (0f,0f))
                throw new Exception("Classic hold threshold changed");
        if (response.Sample(TouchStylusResponse.Classic, true, 8, -4) != (8f,-4f))
            throw new Exception("Classic sustained distance gain differs from Direct");
        response.Sample(TouchStylusResponse.Classic, false, 0, 0);
        if (response.Sample(TouchStylusResponse.Classic, true, 8, -4) != (0f,0f))
            throw new Exception("Classic release does not reset hold/filter");
        if (response.Sample(TouchStylusResponse.Direct, true, float.NaN, 1) != (0f,0f))
            throw new Exception("invalid delta reaches aim");
        var pathological = response.Sample(TouchStylusResponse.Direct, true, float.MaxValue, -float.MaxValue);
        if (!float.IsFinite(pathological.X) || !float.IsFinite(pathological.Y)) throw new Exception("unbounded delta");
        var oldResponse = TouchSettings.StylusResponse;
        try
        {
            TouchSettings.StylusResponse = TouchStylusResponse.Classic;
            var touch = new TouchControls();
            touch.Layout(1920,1080,3);
            touch.PointerDown(1,1400,500);
            touch.PointerMove(1,1408,496,0,0);
            if (touch.TakeAimDelta() != (0f,0f) || touch.PeekAimDelta() != (0f,0f))
                throw new Exception("Classic render pose bypasses canonical held aim");
            touch.PointerMove(1,1416,492,0,0);
            if (touch.TakeAimDelta(useStylusResponse:false) != (8f,-4f))
                throw new Exception("Classic response changed native morph gestures");
            for (int frame = 1; frame <= 11; frame++)
            {
                touch.PointerMove(1,1416 + frame * 8,492,0,0);
                touch.TakeAimDelta();
            }
            touch.PointerUp(1);
            touch.PointerDown(2,1500,500);
            touch.PointerMove(2,1508,496,0,0);
            if (touch.TakeAimDelta() != (0f,0f))
                throw new Exception("new Classic contact inherited prior hold/history between simulation steps");
            touch.ReleaseEverything();
            TouchSettings.ReadSetting("touch_stylus_response","99");
            if (TouchSettings.StylusResponse != TouchStylusResponse.Direct) throw new Exception("invalid response fallback");
            Console.WriteLine("TOUCHCHECK PASS: Direct release/short-flick exactly once, Classic fixed-step hold/filter, morph isolation, finite deltas");
        }
        finally { TouchSettings.StylusResponse = oldResponse; }
    }
}
