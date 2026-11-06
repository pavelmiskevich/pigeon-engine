using Pigeon.AI.Behavior;

namespace Pigeon.AI.Tests;

public sealed class ResponseCurveTests
{
    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(2.0, 1.0)]
    public void Linear_IsClampedToZeroOne(double x, double expected)
    {
        Assert.Equal(expected, new ResponseCurve(CurveKind.Linear).Evaluate(x), 1e-12);
    }

    [Fact]
    public void FromGreaterThanTo_MakesACurveDecrease()
    {
        var curve = new ResponseCurve(CurveKind.Linear, From: 1.0, To: 0.4, Min: 0.2, Max: 1.0);

        Assert.Equal(1.0, curve.Evaluate(0.1), 1e-12);
        Assert.Equal(0.6, curve.Evaluate(0.7), 1e-12);
        Assert.Equal(0.2, curve.Evaluate(1.0), 1e-12);
    }

    [Fact]
    public void Smoothstep_IsFlatAtTheEdgesAndHalfInTheMiddle()
    {
        var curve = new ResponseCurve(CurveKind.Smoothstep, From: 0.2, To: 0.8);

        Assert.Equal(0.0, curve.Evaluate(0.2));
        Assert.Equal(0.5, curve.Evaluate(0.5), 1e-12);
        Assert.Equal(1.0, curve.Evaluate(0.8));
        Assert.True(curve.Evaluate(0.25) < 0.25 / 3);
    }

    [Fact]
    public void Power_RaisesToAnIntegerExponent()
    {
        Assert.Equal(0.125, new ResponseCurve(CurveKind.Power, Exponent: 3).Evaluate(0.5), 1e-12);
    }

    [Fact]
    public void Constant_IgnoresTheInput()
    {
        Assert.Equal(0.3, ResponseCurve.Constant(0.3).Evaluate(123.0));
    }

    [Fact]
    public void Problems_ReportBadParameters()
    {
        Assert.NotEmpty(new ResponseCurve(CurveKind.Linear, From: 0.5, To: 0.5).Problems());
        Assert.NotEmpty(new ResponseCurve(CurveKind.Linear, Min: -0.1).Problems());
        Assert.NotEmpty(new ResponseCurve(CurveKind.Power, Exponent: 0).Problems());
        Assert.NotEmpty(ResponseCurve.Constant(1.5).Problems());
        Assert.Empty(new ResponseCurve(CurveKind.Smoothstep, From: 0.8, To: 0.2).Problems());
    }
}
