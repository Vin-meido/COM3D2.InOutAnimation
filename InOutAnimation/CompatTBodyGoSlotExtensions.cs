using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;

namespace COM3D2.InOutAnimation.Plugin
{
    static class CompatTBodyGoSlotExtensions
    {
        static FieldInfo TBody_goSlot_FieldInfo;
        static FieldInfo Slot_m_slots_FieldInfo;
        static bool isCom3d25 = false;

        static CompatTBodyGoSlotExtensions()
        {
            TBody_goSlot_FieldInfo = typeof(TBody).GetField("goSlot");

            if (TBody_goSlot_FieldInfo == null)
            {
                throw new Exception("Cannot get goSlot field");
            }

            if (TBody_goSlot_FieldInfo.FieldType != typeof(List<TBodySkin>))
            {
                isCom3d25 = true;
                Slot_m_slots_FieldInfo = TBody_goSlot_FieldInfo.FieldType.GetField("m_slots", BindingFlags.Instance | BindingFlags.NonPublic);
                if (Slot_m_slots_FieldInfo == null)
                {
                    throw new Exception("Detected 2.5 TBody but can't get m_slots field");
                }
            }
        }

        static List<TBodySkin> GoSlot_20(TBody tbody)
        {
            return TBody_goSlot_FieldInfo.GetValue(tbody) as List<TBodySkin>;
        }

        static List<List<TBodySkin>> GoSlot_25(TBody tbody)
        {
            var goSlot_obj = TBody_goSlot_FieldInfo.GetValue(tbody);
            return Slot_m_slots_FieldInfo.GetValue(goSlot_obj) as List<List<TBodySkin>>;
        }

        public static TBodySkin GetGoSlot(this TBody tbody, int slot)
        {
            if (isCom3d25)
            {
                return GoSlot_25(tbody)[slot][0];
            }

            return GoSlot_20(tbody)[slot];
        }

        public static IEnumerable<TBodySkin> EnumerateGoSlot(this TBody tbody)
        {
            if (isCom3d25)
            {
                foreach (var slot in GoSlot_25(tbody))
                {
                    yield return slot[0];
                }
            }
            else
            {
                foreach (var slot in GoSlot_20(tbody))
                {
                    yield return slot;
                }
            }
        }


    }

}
