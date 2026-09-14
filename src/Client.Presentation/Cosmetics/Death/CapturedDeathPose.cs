using System;
using System.Collections.Generic;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Cosmetics.Presentation;

public readonly record struct CapturedDeathAppearance(
    Hunter Hunter,
    CosmeticLoadoutIds Cosmetics,
    int Recolor,
    float Alpha);

/// <summary>
/// Presentation-owned copy of the last biped pose that was actually submitted
/// while alive. Its buffers are retained per player and reused across lives.
/// </summary>
public sealed class CapturedDeathPose
{
    private Matrix4[] _nodes = Array.Empty<Matrix4>();
    private float[] _matrixStack = Array.Empty<float>();

    public bool IsValid { get; private set; }
    public Matrix4 Root { get; private set; } = Matrix4.Identity;
    public Matrix4[] Nodes => _nodes;
    public float[] MatrixStack => _matrixStack;
    public Model? Model { get; private set; }
    public CombatActor Actor { get; private set; } = CombatActor.None;
    public CapturedDeathAppearance Appearance { get; private set; }

    public void Capture(Model model, in Matrix4 root,
        ReadOnlySpan<Matrix4> nodes, ReadOnlySpan<float> matrixStack,
        in CapturedDeathAppearance appearance, in CombatActor actor)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (nodes.Length != model.Nodes.Count
            || matrixStack.Length != model.NodeMatrixIds.Count * 16)
            throw new ArgumentException("Captured pose does not match its model skeleton.");
        if (_nodes.Length != nodes.Length) _nodes = new Matrix4[nodes.Length];
        if (_matrixStack.Length != matrixStack.Length)
            _matrixStack = new float[matrixStack.Length];
        nodes.CopyTo(_nodes);
        matrixStack.CopyTo(_matrixStack);
        Root = root;
        Model = model;
        Actor = actor;
        Appearance = appearance;
        IsValid = true;
    }

    public void CaptureSubmitted(Model model, in Matrix4 root,
        Matrix4[]? submittedNodes, float[]? submittedStack,
        in CapturedDeathAppearance appearance, in CombatActor actor)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (submittedNodes != null && submittedNodes.Length != model.Nodes.Count
            || submittedStack != null && submittedStack.Length < model.NodeMatrixIds.Count * 16)
            throw new ArgumentException("Submitted pose does not match its model skeleton.");
        if (_nodes.Length != model.Nodes.Count) _nodes = new Matrix4[model.Nodes.Count];
        int stackLength = model.NodeMatrixIds.Count * 16;
        if (_matrixStack.Length != stackLength) _matrixStack = new float[stackLength];
        for (int i = 0; i < _nodes.Length; i++)
            _nodes[i] = submittedNodes == null ? model.Nodes[i].Animation : submittedNodes[i];
        IReadOnlyList<float> sourceStack = submittedStack == null
            ? model.MatrixStackValues : submittedStack;
        for (int i = 0; i < stackLength; i++) _matrixStack[i] = sourceStack[i];
        Root = root;
        Model = model;
        Actor = actor;
        Appearance = appearance;
        IsValid = true;
    }

    public void Invalidate()
    {
        IsValid = false;
        Model = null;
        Actor = CombatActor.None;
        Appearance = default;
        Root = Matrix4.Identity;
    }
}
