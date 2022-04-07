using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace COM3D2.InOutAnimation.Plugin.Extensions
{
    static class TBodyIKCompatExtensions
    {
        public static Transform GetIKBone_Spine0(this TBody body)
        {
#if !COM3D25
            return body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Spine0);
#else
            return body.fullBodyIK.GetIKBone(FullBodyIKMgr.IKBoneType.Spine0);
#endif
        }

        public static Transform GetIKBone_Pelvis(this TBody body)
        {
#if !COM3D25
            return body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Pelvis);
#else
            return body.fullBodyIK.GetIKBone(FullBodyIKMgr.IKBoneType.Pelvis);
#endif
        }

        public static Transform GetIKBone_Hand_R(this TBody body)
        {
#if !COM3D25
            return body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Hand_R);
#else
            return body.fullBodyIK.GetIKBone(FullBodyIKMgr.IKBoneType.Hand_R);
#endif
        }

        public static Transform GetIKBone_Hand_L(this TBody body)
        {
#if !COM3D25
            return body.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Hand_L);
#else
            return body.fullBodyIK.GetIKBone(FullBodyIKMgr.IKBoneType.Hand_L);
#endif
        }

        public static Transform GetIKBone_Head(this Maid maid)
        {
#if !COM3D25
            return maid.IKCtrl.GetIKBone(FullBodyIKCtrl.IKBoneType.Head);
#else
            return maid.fullBodyIK.GetIKBone(FullBodyIKMgr.IKBoneType.Head);
#endif
        }
    }
}
