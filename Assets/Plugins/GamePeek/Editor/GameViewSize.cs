// Derived from com.unity.testframework.graphics (MIT licence).
// Only the SetCustomSize / SelectSize paths are retained.
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;

namespace GamePeek
{
    public static class GameViewSize
    {
        // PlayModeView is the base class of GameView in Unity 2019+.
        static readonly Type s_PlayModeViewType =
            Type.GetType("UnityEditor.PlayModeView,UnityEditor");

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static EditorWindow GetMainGameView()
        {
            var m = s_PlayModeViewType?
                .GetMethod("GetMainPlayModeView", BindingFlags.NonPublic | BindingFlags.Static);
            return m?.Invoke(null, null) as EditorWindow;
        }

        static object CurrentGroup()
        {
            var sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
            var instance  = sizesType?.BaseType
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            return instance?.GetType()
                .GetProperty("currentGroup", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(instance);
        }

        static object NewSizeObj(int width, int height)
        {
            var sizeType     = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
            var sizeEnumType = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
            var ctor = sizeType?.GetConstructor(
                new[] { sizeEnumType, typeof(int), typeof(int), typeof(string) });
            // enum value 1 == FixedResolution
            return ctor?.Invoke(new object[] { 1, width, height, "GamePeekCapture" });
        }

        static object FindExistingSlot()
        {
            var group   = CurrentGroup();
            var customs = group?.GetType()
                .GetField("m_Custom", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(group);
            if (customs == null) return null;

            var itr = (IEnumerator)customs.GetType().GetMethod("GetEnumerator")
                .Invoke(customs, null);
            while (itr.MoveNext())
            {
                var label = itr.Current?.GetType()
                    .GetField("m_BaseText", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(itr.Current) as string;
                if (label == "GamePeekCapture") return itr.Current;
            }
            return null;
        }

        static int IndexOf(object sizeObj)
        {
            var group  = CurrentGroup();
            var method = group?.GetType().GetMethod("IndexOf", BindingFlags.Public | BindingFlags.Instance);
            int index  = (int)(method?.Invoke(group, new[] { sizeObj }) ?? 0);

            var builtinList = group?.GetType()
                .GetField("m_Builtin", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(group);
            var contains = builtinList?.GetType().GetMethod("Contains");
            if ((bool)(contains?.Invoke(builtinList, new[] { sizeObj }) ?? false))
                return index;

            var getBuiltin = group?.GetType().GetMethod("GetBuiltinCount");
            index += (int)(getBuiltin?.Invoke(group, null) ?? 0);
            return index;
        }

        // ── Public surface (matches the package's API) ────────────────────────

        /// <summary>
        /// Creates or reuses the "GamePeekCapture" custom Game View size slot
        /// and sets it to <paramref name="width"/> × <paramref name="height"/>.
        /// Returns the size object required by <see cref="SelectSize"/>.
        /// </summary>
        public static object SetCustomSize(int width, int height)
        {
            var slot = FindExistingSlot();
            if (slot != null)
            {
                var slotType = slot.GetType();
                slotType.GetField("m_Width",  BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(slot, width);
                slotType.GetField("m_Height", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(slot, height);
            }
            else
            {
                slot = NewSizeObj(width, height);
                var group = CurrentGroup();
                group?.GetType()
                    .GetMethod("AddCustomSize", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(group, new[] { slot });
            }
            return slot;
        }

        /// <summary>Selects <paramref name="sizeObj"/> in the main Game View.</summary>
        public static void SelectSize(object sizeObj)
        {
            var gameView = GetMainGameView();
            if (gameView == null) return;

            int index = IndexOf(sizeObj);
            gameView.GetType()
                .GetMethod("SizeSelectionCallback", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(gameView, new[] { (object)index, sizeObj });
        }

        /// <summary>
        /// Returns the index currently selected in the main Game View's size
        /// drop-down, or -1 when it cannot be determined (no Game View open,
        /// or Unity internals changed).
        /// </summary>
        public static int GetSelectedIndex()
        {
            try
            {
                var gameView = GetMainGameView();
                var prop = gameView?.GetType().GetProperty("selectedSizeIndex",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return -1;
                return (int)prop.GetValue(gameView);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Selects the size at <paramref name="index"/> in the main Game View's
        /// drop-down (an index previously returned by <see cref="GetSelectedIndex"/>).
        /// Best-effort — silently does nothing when the index is invalid or
        /// reflection fails.
        /// </summary>
        public static void SelectIndex(int index)
        {
            if (index < 0) return;
            try
            {
                var gameView = GetMainGameView();
                gameView?.GetType()
                    .GetMethod("SizeSelectionCallback", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(gameView, new object[] { index, null });
            }
            catch
            {
                // Unity internals may differ between versions — same defensive
                // posture as the reflection helpers above.
            }
        }
    }
}
