#include <yoga/Yoga.h>
#include <array>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>

// Neutral, versioned C ABI. Callers serialize each root on its owner thread.
// Optional measurement callbacks may use immutable host data only, never Unity
// or guest state. The caller keeps callback/context alive until layout returns.
#define PUI_EXPORT extern "C" __declspec(dllexport)
namespace {
constexpr int MaxNodes = 1024, MaxDepth = 24, MaxMeasures = 4096;
std::atomic<int> liveRoots{0};
struct Style {
  int32_t parent, row, absolute, align, justify;
  float width, height, left, top, grow, shrink, gap;
  float paddingLeft, paddingTop, paddingRight, paddingBottom;
  float intrinsicWidth, intrinsicHeight;
};
struct Rect { float x, y, width, height; };
struct Metrics { int32_t nodes, measures, rebuilt; };
struct Root {
  YGConfigRef config = YGConfigNew();
  YGNodeRef tree = nullptr;
  std::array<Style, MaxNodes> styles{};
  std::array<Rect, MaxNodes> rects{};
  int count = 0;
  Root() { YGConfigSetUseWebDefaults(config, true); YGConfigSetPointScaleFactor(config, 0); ++liveRoots; }
  ~Root() { if (tree) YGNodeFreeRecursive(tree); YGConfigFree(config); --liveRoots; }
};
using MeasureCallback = int (*)(void*, int, float, int, float, int, float*, float*);
struct Measure { float width, height; int* calls; int index; MeasureCallback callback; void* context; };
YGSize measure(YGNodeConstRef node, float width, YGMeasureMode wm, float height, YGMeasureMode hm) {
  auto* data = static_cast<Measure*>(YGNodeGetContext(node));
  if (++*data->calls > MaxMeasures) throw MaxMeasures;
  if (data->callback) {
    float w = 0, h = 0;
    if (!data->callback(data->context, data->index, width, static_cast<int>(wm), height, static_cast<int>(hm), &w, &h) ||
        !std::isfinite(w) || !std::isfinite(h) || w < 0 || h < 0 || w > 4096 || h > 4096) throw MaxMeasures;
    return {w, h};
  }
  // This slice uses immutable single-line metrics; wrapping is deliberately absent.
  return {wm == YGMeasureModeExactly ? width : wm == YGMeasureModeAtMost ? std::min(width, data->width) : data->width,
          hm == YGMeasureModeExactly ? height : hm == YGMeasureModeAtMost ? std::min(height, data->height) : data->height};
}
bool valid(const Style* styles, int count) {
  if (!styles || count < 1 || count > MaxNodes) return false;
  std::array<int, MaxNodes> depths{};
  std::array<bool, MaxNodes> measured{};
  for (int i = 0; i < count; ++i) {
    const auto& s = styles[i];
    if ((i == 0 ? s.parent != -1 : s.parent < 0 || s.parent >= i) ||
        s.row < 0 || s.row > 1 || s.absolute < 0 || s.absolute > 1 ||
        s.align < 1 || s.align > 4 || s.justify < 0 || s.justify > 3) return false;
    depths[i] = i == 0 ? 1 : depths[s.parent] + 1;
    if (depths[i] > MaxDepth || (i && measured[s.parent])) return false;
    const std::array<float, 11> values = {s.width, s.height, s.grow, s.shrink, s.gap,
      s.paddingLeft, s.paddingTop, s.paddingRight, s.paddingBottom, s.intrinsicWidth, s.intrinsicHeight};
    for (float value : values) if (!std::isfinite(value) || value < -1 || value > 4096) return false;
    if (!std::isfinite(s.left) || !std::isfinite(s.top) || std::abs(s.left) > 8192 || std::abs(s.top) > 8192) return false;
    if (s.grow < 0 || s.grow > 100 || s.shrink < 0 || s.shrink > 100 || s.gap < 0 ||
        s.paddingLeft < 0 || s.paddingTop < 0 || s.paddingRight < 0 || s.paddingBottom < 0 ||
        ((s.intrinsicWidth < 0) != (s.intrinsicHeight < 0))) return false;
    measured[i] = s.intrinsicWidth >= 0;
  }
  return true;
}
}
PUI_EXPORT int pui_yoga_version() { return 2; }
PUI_EXPORT int pui_yoga_live_roots() { return liveRoots.load(); }
PUI_EXPORT void* pui_yoga_create() { try { return new Root(); } catch (...) { return nullptr; } }
PUI_EXPORT void pui_yoga_destroy(void* root) { delete static_cast<Root*>(root); }
PUI_EXPORT int pui_yoga_layout(void* pointer, const Style* styles, int count, Rect* output, Metrics* metrics, MeasureCallback callback, void* context) {
  if (!pointer || !output || !metrics || !valid(styles, count)) return 1;
  auto& root = *static_cast<Root*>(pointer);
  *metrics = {count, 0, 0};
  if (!callback && root.count == count && std::memcmp(styles, root.styles.data(), count * sizeof(Style)) == 0) {
    std::memcpy(output, root.rects.data(), count * sizeof(Rect));
    return 0;
  }
  // Stage a whole bounded transaction. Failure never mutates the retained tree.
  YGNodeRef candidate = nullptr;
  try {
    std::array<YGNodeRef, MaxNodes> nodes{};
    std::array<Measure, MaxNodes> measurements{};
    std::array<Rect, MaxNodes> rects{};
    for (int i = 0; i < count; ++i) {
      const auto& s = styles[i];
      auto n = nodes[i] = YGNodeNewWithConfig(root.config);
      if (!i) candidate = n;
      else YGNodeInsertChild(nodes[s.parent], n, YGNodeGetChildCount(nodes[s.parent]));
      YGNodeStyleSetFlexDirection(n, s.row ? YGFlexDirectionRow : YGFlexDirectionColumn);
      YGNodeStyleSetAlignItems(n, static_cast<YGAlign>(s.align));
      YGNodeStyleSetJustifyContent(n, static_cast<YGJustify>(s.justify));
      YGNodeStyleSetFlexGrow(n, s.grow); YGNodeStyleSetFlexShrink(n, s.shrink);
      YGNodeStyleSetGap(n, YGGutterAll, s.gap);
      if (s.width >= 0) YGNodeStyleSetWidth(n, s.width);
      if (s.height >= 0) YGNodeStyleSetHeight(n, s.height);
      if (s.absolute) {
        YGNodeStyleSetPositionType(n, YGPositionTypeAbsolute);
        YGNodeStyleSetPosition(n, YGEdgeLeft, s.left); YGNodeStyleSetPosition(n, YGEdgeTop, s.top);
      }
      YGNodeStyleSetPadding(n, YGEdgeLeft, s.paddingLeft); YGNodeStyleSetPadding(n, YGEdgeTop, s.paddingTop);
      YGNodeStyleSetPadding(n, YGEdgeRight, s.paddingRight); YGNodeStyleSetPadding(n, YGEdgeBottom, s.paddingBottom);
      if (s.intrinsicWidth >= 0) {
        measurements[i] = {s.intrinsicWidth, s.intrinsicHeight, &metrics->measures, i, callback, context};
        YGNodeSetContext(n, &measurements[i]); YGNodeSetMeasureFunc(n, measure);
      }
    }
    YGNodeCalculateLayout(candidate, YGUndefined, YGUndefined, YGDirectionLTR);
    if (metrics->measures > MaxMeasures) { YGNodeFreeRecursive(candidate); return 2; }
    for (int i = 0; i < count; ++i) {
      auto n = nodes[i];
      rects[i] = {YGNodeLayoutGetLeft(n), YGNodeLayoutGetTop(n), YGNodeLayoutGetWidth(n), YGNodeLayoutGetHeight(n)};
      for (float v : {rects[i].x, rects[i].y, rects[i].width, rects[i].height})
        if (!std::isfinite(v) || std::abs(v) > 8192) { YGNodeFreeRecursive(candidate); return 3; }
      // Retained nodes must never retain pointers into this invocation's stack.
      if (YGNodeHasMeasureFunc(n)) { YGNodeSetMeasureFunc(n, nullptr); YGNodeSetContext(n, nullptr); }
    }
    if (root.tree) YGNodeFreeRecursive(root.tree);
    root.tree = candidate; root.count = count; root.rects = rects;
    std::memcpy(root.styles.data(), styles, count * sizeof(Style));
    std::memcpy(output, rects.data(), count * sizeof(Rect)); metrics->rebuilt = 1;
    return 0;
  } catch (...) { if (candidate) YGNodeFreeRecursive(candidate); return 4; }
}
