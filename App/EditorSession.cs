// EditorSession.cs — W3 编辑态会话：New / Edit(id) 两态、脏标记、快照、CanLeave 守卫（无 UI 依赖）
// 只被 MainForm 使用；不碰 Store/schema；对话框由宿主通过 ConfirmLeave 委托提供（便于自检不弹窗）。
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public enum EditorMode { New, Edit }
    public enum LeaveAction { Save, Discard, Stay }

    public sealed class EditorSession
    {
        public EditorMode Mode = EditorMode.New;
        public long Id;              // 正在编辑的待办 id（New 时为 0）
        public bool Dirty;           // 有未保存修改
        public TodoItem Snapshot;    // 进入编辑时的字段快照（取消/回滚用）
        public Func<LeaveAction> ConfirmLeave;   // 宿主注入：脏数据时询问 保存/放弃/继续

        public bool IsEditing { get { return Mode == EditorMode.Edit; } }

        public void BeginNew()
        {
            Mode = EditorMode.New; Id = 0; Dirty = false; Snapshot = null;
        }

        public void BeginEdit(TodoItem t)
        {
            Mode = EditorMode.Edit; Id = t == null ? 0 : t.Id; Dirty = false;
            Snapshot = t == null ? null : Clone(t);
        }

        public void MarkDirty() { if (IsEditing) Dirty = true; }

        public void End() { Mode = EditorMode.New; Id = 0; Dirty = false; Snapshot = null; }

        // 切行/筛选/隐藏/退出前调用；返回 false 表示用户选择"继续留在编辑"
        public bool CanLeave()
        {
            if (!IsEditing || !Dirty) return true;
            LeaveAction action = ConfirmLeave == null ? LeaveAction.Discard : ConfirmLeave();
            if (action == LeaveAction.Stay) return false;
            if (action == LeaveAction.Discard) { Dirty = false; }
            // Save 情况由宿主在 ConfirmLeave 内完成保存（保存后 Dirty=false）
            return true;
        }

        private static TodoItem Clone(TodoItem t)
        {
            TodoItem c = new TodoItem();
            c.Id = t.Id; c.Title = t.Title; c.Note = t.Note; c.Importance = t.Importance;
            c.DueUtc = t.DueUtc; c.RemindUtc = t.RemindUtc; c.Recurrence = t.Recurrence;
            c.RecurrenceNextUtc = t.RecurrenceNextUtc; c.Pinned = t.Pinned; c.Done = t.Done;
            c.CreatedUtc = t.CreatedUtc; c.UpdatedUtc = t.UpdatedUtc;
            return c;
        }
    }

    // IME 组字态检测：组字中 Enter/Esc 必须交给输入法，不得保存/退出编辑
    public static class ImeHelper
    {
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionString(IntPtr hIMC, int index, StringBuilder buf, int len);
        private const int GCS_COMPSTR = 0x0008;

        public static bool IsComposing(Control c)
        {
            if (c == null || !c.IsHandleCreated) return false;
            IntPtr himc = IntPtr.Zero;
            try
            {
                himc = ImmGetContext(c.Handle);
                if (himc == IntPtr.Zero) return false;
                int len = ImmGetCompositionString(himc, GCS_COMPSTR, null, 0);
                return len > 0;
            }
            catch { return false; }
            finally { if (himc != IntPtr.Zero) { try { ImmReleaseContext(c.Handle, himc); } catch { } } }
        }
    }
}
