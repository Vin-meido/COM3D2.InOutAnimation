using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace COM3D2.InOutAnimation.Plugin.Extensions
{
    static class TBodyIKCompatExtensions
    {
        static object Spine0;
        static object Pelvis;
        static object Hand_R;
        static object Hand_L;
        static object Head;

        static Type IKBoneTypeEnumType;
        static MethodInfo GetIKBoneMethodInfo;
        static PropertyInfo IKControllerInfo;
        static PropertyInfo MaidIKControllerInfo;


        static TBodyIKCompatExtensions()
        {
            if(!Init_20())
            {
                Init_25();
            }

            Spine0 = Enum.Parse(IKBoneTypeEnumType, "Spine0");
            Pelvis = Enum.Parse(IKBoneTypeEnumType, "Pelvis");
            Hand_R = Enum.Parse(IKBoneTypeEnumType, "Hand_R");
            Hand_L = Enum.Parse(IKBoneTypeEnumType, "Hand_L");
            Head = Enum.Parse(IKBoneTypeEnumType, "Head");
        }

        static bool Init_20()
        {
            var asm = typeof(TBody).Assembly;

            IKControllerInfo = typeof(TBody).GetProperty("IKCtrl");
            if(IKControllerInfo == null)
            {
                return false;
            }

            MaidIKControllerInfo = typeof(Maid).GetProperty("IKCtrl");

            IKBoneTypeEnumType = asm.GetType("FullBodyIKCtrl+IKBoneType");
            GetIKBoneMethodInfo = IKControllerInfo.PropertyType.GetMethod("GetIKBone", new Type[] { IKBoneTypeEnumType });

            return true;
        }

        static bool Init_25()
        {
            var asm = typeof(TBody).Assembly;

            IKControllerInfo = typeof(TBody).GetProperty("fullBodyIK");
            if (IKControllerInfo == null)
            {
                return false;
            }

            MaidIKControllerInfo = typeof(Maid).GetProperty("fullBodyIK");

            IKBoneTypeEnumType = asm.GetType("FullBodyIKMgr+IKBoneType");
            GetIKBoneMethodInfo = IKControllerInfo.PropertyType.GetMethod("GetIKBone", new Type[] { IKBoneTypeEnumType });

            return true;
        }

        static object CompatGetIKMgr(this TBody body)
        {
            return IKControllerInfo.GetValue(body, null);
        }

        static Transform CompatGetIKBone(this TBody body, object bone)
        {
            var mgr = body.CompatGetIKMgr();
            var result = GetIKBoneMethodInfo.Invoke(mgr, new object[] { bone });
            return result as Transform;
        }

        static object CompatGetIKMgr(this Maid maid)
        {
            return MaidIKControllerInfo.GetValue(maid, null);
        }

        static Transform CompatGetIKBone(this Maid maid, object bone)
        {
            var mgr = maid.CompatGetIKMgr();
            var result = GetIKBoneMethodInfo.Invoke(mgr, new object[] { bone });
            return result as Transform;
        }

        public static Transform GetIKBone_Spine0(this TBody body)
        {
            return body.CompatGetIKBone(Spine0);
        }

        public static Transform GetIKBone_Pelvis(this TBody body)
        {
            return body.CompatGetIKBone(Pelvis);
        }

        public static Transform GetIKBone_Hand_R(this TBody body)
        {
            return body.CompatGetIKBone(Hand_R);
        }

        public static Transform GetIKBone_Hand_L(this TBody body)
        {
            return body.CompatGetIKBone(Hand_L);
        }

        public static Transform GetIKBone_Head(this Maid maid)
        {
            return maid.CompatGetIKBone(Head);
        }
    }
}
