using System;
using System.Buffers;
using System.Diagnostics;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {

        internal void UpdateSpireAltAttack()
        {
            Matrix4 transform = GetTransformMatrix(_spireAltFacing, _spireAltUp);
            _altModel.Model.AnimateNodes(index: 0, useNodeTransform: false, transform, Vector3.One, _altModel.AnimInfo);
            _spireRockPosL = _spireAltNodes[0]!.Animation.Row3.Xyz + Position;
            _spireRockPosR = _spireAltNodes[1]!.Animation.Row3.Xyz + Position;
        }
    }
}
