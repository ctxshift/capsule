using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Capsule.Yoga
{
    public interface IYogaMeasurer
    {
        void Measure(int node, float width, int widthMode, float height, int heightMode, out float measuredWidth, out float measuredHeight);
    }
    // Prototype ABI model. Negative dimensions mean auto; intrinsic dimensions
    // are immutable, premeasured single-line text bounds, never Unity callbacks.
    [StructLayout(LayoutKind.Sequential)]
    public struct YogaStyle
    {
        public int Parent, Row, Absolute, Align, Justify;
        public float Width, Height, Left, Top, Grow, Shrink, Gap;
        public float PaddingLeft, PaddingTop, PaddingRight, PaddingBottom;
        public float IntrinsicWidth, IntrinsicHeight;
        public static YogaStyle Box(int parent, float width = -1, float height = -1) => new YogaStyle
        {
            Parent = parent, Width = width, Height = height, Align = 4, Shrink = 1,
            IntrinsicWidth = -1, IntrinsicHeight = -1
        };
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct YogaRect { public float X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    public struct YogaMetrics { public int Nodes, Measurements, Rebuilt; }

    public sealed class NativeYoga : IDisposable
    {
        const string Library = "portable_ui_yoga";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int MeasureCallback(IntPtr context, int node, float width, int widthMode, float height, int heightMode, out float measuredWidth, out float measuredHeight);
        static readonly MeasureCallback Callback = Measure;
        sealed class MeasureContext { public IYogaMeasurer Measurer; public string Error; }
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_yoga_version();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_yoga_live_roots();
        public static int LiveRoots => pui_yoga_live_roots();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pui_yoga_create();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void pui_yoga_destroy(IntPtr root);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int pui_yoga_layout(IntPtr root,
            [In] YogaStyle[] styles, int count, [Out] YogaRect[] rectangles, out YogaMetrics metrics, MeasureCallback callback, IntPtr context);
        readonly int owner = Thread.CurrentThread.ManagedThreadId;
        IntPtr root;
        public NativeYoga()
        {
            if (pui_yoga_version() != 2) throw new InvalidOperationException("Yoga ABI mismatch.");
            root = pui_yoga_create();
            if (root == IntPtr.Zero) throw new InvalidOperationException("Yoga allocation failed.");
        }
        void CheckOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != owner) throw new InvalidOperationException("Yoga owner thread required.");
        }
        public bool TryLayout(YogaStyle[] styles, out YogaRect[] rectangles, out YogaMetrics metrics, out double milliseconds, out string error, IYogaMeasurer measurer = null)
        {
            CheckOwner();
            if (root == IntPtr.Zero) throw new ObjectDisposedException(nameof(NativeYoga));
            metrics = default; milliseconds = 0; rectangles = null; error = null;
            if (styles == null || styles.Length < 1 || styles.Length > UiSession.ExtendedLayoutNodes) { error = "Yoga node quota."; return false; }
            var output = new YogaRect[styles.Length];
            long start = Stopwatch.GetTimestamp();
            var context = measurer == null ? null : new MeasureContext { Measurer = measurer };
            var handle = context == null ? default : GCHandle.Alloc(context);
            int result;
            try { result = pui_yoga_layout(root, styles, styles.Length, output, out metrics, context == null ? null : Callback, context == null ? IntPtr.Zero : GCHandle.ToIntPtr(handle)); }
            finally { if (handle.IsAllocated) handle.Free(); }
            milliseconds = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            if (result != 0) { error = context?.Error ?? "Yoga transaction rejected: " + result; return false; }
            rectangles = output;
            return true;
        }
#if UNITY_5_3_OR_NEWER
        [AOT.MonoPInvokeCallback(typeof(MeasureCallback))]
#endif
        static int Measure(IntPtr pointer, int node, float width, int widthMode, float height, int heightMode, out float measuredWidth, out float measuredHeight)
        {
            measuredWidth = measuredHeight = 0;
            MeasureContext context = null;
            try
            {
                context = (MeasureContext)GCHandle.FromIntPtr(pointer).Target;
                context.Measurer.Measure(node, width, widthMode, height, heightMode, out measuredWidth, out measuredHeight);
                return 1;
            }
            catch (Exception failure) { if (context != null) context.Error = failure.Message; return 0; }
        }
        public void Dispose()
        {
            CheckOwner();
            if (root != IntPtr.Zero) { pui_yoga_destroy(root); root = IntPtr.Zero; }
        }
    }
}
