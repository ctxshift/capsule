using System;
using PortableUi.Package;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace PortableUi.Unity
{
 public sealed class UiWindowParts
 { public Func<NativeUiWidget,Image> Frame,Rule; public Func<NativeUiWidget,RectTransform> Header; public Action<RectTransform,float,float,Color> Shadow; }
 public sealed class PortableUiControlSkin : IDisposable
 {
 readonly UiControlTheme theme; readonly TMP_FontAsset font; readonly UiWindowParts parts; readonly Assets art; Material buttonSoftLight;
 sealed class Assets {readonly Func<string,Sprite> resolve;public Assets(Func<string,Sprite> r){resolve=r;}public bool TryGet(string key,out Sprite sprite){sprite=resolve(key);if(!sprite)throw new InvalidOperationException("Missing theme sprite: "+key);return true;}}
 public PortableUiControlSkin(UiControlTheme definition,TMP_FontAsset exactFont,Func<string,Sprite> sprites,UiWindowParts windowParts){theme=definition;font=exactFont;art=new Assets(sprites);parts=windowParts;}
 float N(string group,string key)=>theme.Number(group,key);
 Color32 C(string group,string key){var c=theme.Color(group,key);return new Color32(c[0],c[1],c[2],c[3]);}
 static Vector4 V(Color color)=>new Vector4(color.r,color.g,color.b,color.a);
 float Slice(string group,string field){string key=theme.String(group,field);return theme.AssetNumber(key,"border")/theme.AssetNumber(key,"edge");}
 public bool Apply(NativeUiWidget widget,UiNode node)
 {var root=widget.Rect;
            if(node.Kind==UiNodeKind.Image && widget.Image)
            {
                float gain=theme.ImageBrightness(node.AssetKey);
                var tint=widget.Image.GetComponent<PortableUiBrightness>();
                if(gain!=1 && !tint)tint=widget.Image.gameObject.AddComponent<PortableUiBrightness>();
                if(tint)tint.ThemeFactor=gain;
            }
            if(node.Kind==UiNodeKind.Box && widget.Image) widget.Image.color=Color.clear;
            if(node.Kind==UiNodeKind.Box && theme.TrySurfaceColor(node.StyleKey,out var surface))
            {
                widget.Image.sprite=null;
                widget.Image.color=new Color32(surface[0],surface[1],surface[2],surface[3]);
                return true;
            }
            if (node.Kind == UiNodeKind.Button && (node.StyleKey == theme.String("components","button") || string.IsNullOrEmpty(node.StyleKey)))
            {
                var control = root.GetComponent<Button>(); control.transition = Selectable.Transition.None;
                widget.Image.color = C("button","background");
                var tint = ChildImage(root,"Theme button hover"); Stretch(tint.rectTransform,N("button","highlightInset"));
                var texture = ChildImage(root,"Theme button texture"); Stretch(texture.rectTransform,N("button","highlightInset"));
                art.TryGet(theme.String("button","highlight"),out var highlight); texture.sprite = highlight;
                if(!buttonSoftLight)
                {
                    var shader=Resources.Load<Shader>("PortableUiSoftLight");
                    if(!shader)throw new System.InvalidOperationException("Missing native soft-light shader.");
                    buttonSoftLight=new Material(shader){name="Scoped exact button soft-light",hideFlags=HideFlags.DontSave};
                    buttonSoftLight.SetVector("_BaseRgb",V(C("button","background")));
                    buttonSoftLight.SetVector("_OverlayRgba",V(C("button","hover")));
                }
                texture.material=buttonSoftLight;
                var border = ChildImage(root,"Theme button frame"); Stretch(border.rectTransform,0);
                art.TryGet(theme.String("button","frame"),out var frame); border.sprite = frame;
                border.type = Image.Type.Sliced; border.fillCenter=false; border.pixelsPerUnitMultiplier=Slice("button","frame");
                var focus = FocusFrame(root);
                var feedback=root.GetComponent<PortableUiControlFeedback>()??root.gameObject.AddComponent<PortableUiControlFeedback>();
                feedback.Configure(tint,texture,focus,Color.clear,C("button","foreground"),N("button","highlightOpacity"));
            }
            else if (node.Kind == UiNodeKind.Range && (node.StyleKey == theme.String("components","slider") || string.IsNullOrEmpty(node.StyleKey)))
            {
                var slider = root.GetComponent<Slider>(); slider.transition=Selectable.Transition.None;
                var track = root.Find("Range Track").GetComponent<PortableUiRoundedImage>();
                track.rectTransform.sizeDelta=new Vector2(0,N("range","trackHeight"));
                ((RectTransform)root.Find("Range Fill Area")).sizeDelta=new Vector2(0,N("range","trackHeight"));
                track.color = C("range","track"); track.Radius=N("range","trackRadius"); track.BorderWidth=N("range","borderWidth"); track.BorderColor=C("range","border"); track.SetVerticesDirty();
                var fill=root.Find("Range Fill Area/Range Fill").GetComponent<PortableUiRoundedImage>();
                fill.color=C("range","fill"); fill.Radius=N("range","trackRadius"); fill.raycastTarget=false;
                var thumb=root.Find("Range Handle Area/Range Handle").GetComponent<PortableUiRoundedImage>();
                thumb.rectTransform.sizeDelta=new Vector2(N("range","thumbWidth"),N("range","thumbHeight")-node.Rect.Height/64f);
                thumb.color=C("range","fill"); thumb.Radius=N("range","thumbRadius"); thumb.raycastTarget=false;
                var feedback=root.GetComponent<PortableUiControlFeedback>()??root.gameObject.AddComponent<PortableUiControlFeedback>();
                feedback.Configure(null,null,FocusFrame(root),Color.clear);
            }
            else if (node.Kind == UiNodeKind.Select && (node.StyleKey == theme.String("components","select") || string.IsNullOrEmpty(node.StyleKey)))
            {
                var select = root.GetComponent<TMP_Dropdown>(); select.transition=Selectable.Transition.None;
                widget.Image.sprite=null; widget.Image.color=C("select","background");
                RectangleBorder(root,"Theme select border",N("select","borderWidth"),C("select","border"),0);
                var label=select.captionText; StyleSelectLabel(label); Stretch(label.rectTransform,0);
                label.rectTransform.offsetMin=new Vector2(N("select","paddingLeft"),0); label.rectTransform.offsetMax=new Vector2(-N("select","paddingRight"),0);
                root.Find("Arrow").gameObject.SetActive(false);
                var arrow=root.Find("Theme select chevron");
                if(!arrow){var obj=new GameObject("Theme select chevron",typeof(RectTransform),typeof(PortableUiChevron));obj.transform.SetParent(root,false);arrow=obj.transform;}
                var chevron=arrow.GetComponent<PortableUiChevron>();chevron.color=C("select","foreground");chevron.raycastTarget=false;
                TopRect((RectTransform)arrow,node.Rect.Width/64f-N("select","paddingRight")+6,10,N("select","arrowWidth"),N("select","arrowHeight"));
                var template=select.template; template.GetComponent<Image>().color=C("select","background");
                template.sizeDelta=new Vector2(0,N("select","itemHeight")*Mathf.Min(16,node.Text.Split('\n').Length)+4);
                var item=select.itemText; StyleSelectLabel(item);
                var itemRoot=item.transform.parent.GetComponent<RectTransform>();itemRoot.sizeDelta=new Vector2(0,N("select","itemHeight"));
                var background=itemRoot.Find("Item Background").GetComponent<Image>();background.color=C("select","background");
                itemRoot.Find("Item Checkmark").gameObject.SetActive(false);
                Stretch(item.rectTransform,0); item.rectTransform.offsetMin=new Vector2(4,0);item.rectTransform.offsetMax=new Vector2(-4,0);
                var feedback=root.GetComponent<PortableUiControlFeedback>()??root.gameObject.AddComponent<PortableUiControlFeedback>();
                feedback.Configure(null,null,FocusFrame(root),Color.clear);
            }
            else if (node.Kind == UiNodeKind.Window)
            {
                root.GetComponent<Image>().color = C("window","background");
                parts.Shadow(root,N("window","shadowOffset"),N("window","shadowBlur"),C("window","shadowColor"));
                var backingTransform = root.Find("Theme window backing");
                if (!backingTransform)
                {
                    var backingObject = new GameObject("Theme window backing", typeof(RectTransform), typeof(RawImage));
                    backingObject.transform.SetParent(root, false);
                    backingTransform = backingObject.transform;
                }
                art.TryGet(theme.String("window","backing"), out var backingSprite);
                var texture = backingTransform.GetComponent<RawImage>();
                texture.texture = backingSprite.texture;
                texture.color = new Color(1,1,1,N("window","backingOpacity"));
                texture.raycastTarget = false;
                float ratio = (node.Rect.Width / 64f - 2*N("window","backingInset")) / (node.Rect.Height / 64f - 2*N("window","backingInset")) /
                    ((float)backingSprite.texture.width / backingSprite.texture.height);
                texture.uvRect = ratio < 1 ? new Rect((1 - ratio) / 2, 0, ratio, 1) :
                    new Rect(0, (1 - 1 / ratio) / 2, 1, 1 / ratio);
                Stretch(texture.rectTransform,N("window","backingInset"));
                texture.transform.SetAsFirstSibling();
                var frame = parts.Frame(widget);
                art.TryGet(theme.String("window","frame"),out var windowFrame); frame.sprite = windowFrame;
                frame.type = Image.Type.Sliced;
                frame.fillCenter = false;
                frame.pixelsPerUnitMultiplier = Slice("window","frame");
                frame.color = Color.white;
                // Original panel edges require alpha-aware interpolation; other
                // artwork keeps its existing native minification policy.
                if(theme.AssetAlphaAware(theme.String("window","frame")) && !frame.GetComponent<PortableUiAlphaAwareImage>())frame.gameObject.AddComponent<PortableUiAlphaAwareImage>();
                var header = parts.Header(widget);
                TopRect(header, N("components","windowInset"),N("components","titleTop"),node.Rect.Width/64f-2*N("components","windowInset"),N("components","titleHeight"));
                header.GetComponent<Image>().color = Color.clear;
                Stretch(widget.Label.rectTransform, 0);
                widget.Label.font=font; widget.Label.fontSize=N("window","titleSize");
                widget.Label.fontStyle = FontStyles.SmallCaps;
                // TMP spacing is a percentage of the font size; CSS uses 2 px.
                widget.Label.characterSpacing = 100*N("window","titleSpacing")/N("window","titleSize");
                string alignment=theme.String("window","titleAlignment");
                widget.Label.alignment=alignment=="center"?TextAlignmentOptions.Midline:alignment=="right"?TextAlignmentOptions.MidlineRight:TextAlignmentOptions.MidlineLeft;
                widget.Label.margin = Vector4.zero;
                widget.Label.color = C("window","titleColor");
                var rule = parts.Rule(widget);
                TopRect(rule.rectTransform, N("components","windowInset"),N("window","ruleTop"),node.Rect.Width/64f-2*N("components","windowInset"),N("window","ruleHeight"));
                rule.sprite = null;
                // Display-space bronze calibrated at the reference scale. A
                // translucent bright bronze blends much brighter in linear UI.
                rule.color = C("window","ruleColor");
                var close = widget.CloseButton.GetComponent<Image>();
                close.transform.SetParent(root, false);
                close.transform.SetAsLastSibling();
                TopRect(close.rectTransform, node.Rect.Width/64f-N("window","closeInset")-N("window","closeSize"),N("window","closeInset"),N("window","closeSize"),N("window","closeSize"));
                art.TryGet(theme.String("window","closeFrame"), out var closeFrame);
                close.sprite = closeFrame;
                close.type = Image.Type.Sliced;
                close.fillCenter = false;
                close.pixelsPerUnitMultiplier=Slice("window","closeFrame");
                close.color = Color.white;
                var cross = ChildImage(close.rectTransform, "Theme close cross");
                art.TryGet(theme.String("window","closeCross"), out var closeX);
                cross.sprite = closeX;
                cross.preserveAspect = true;
                TopRect(cross.rectTransform, N("window","crossInset"),N("window","crossInset"),N("window","crossSize"),N("window","crossSize"));
                widget.CloseButton.transition=Selectable.Transition.None;
                var closeHover=ChildImage(close.rectTransform,"Theme close hover");Stretch(closeHover.rectTransform,4);closeHover.transform.SetAsFirstSibling();
                var closeFeedback=close.GetComponent<PortableUiControlFeedback>()??close.gameObject.AddComponent<PortableUiControlFeedback>();
                closeFeedback.Configure(closeHover,null,FocusFrame(close.rectTransform),C("button","hover"));
            }
 else if(node.Kind==UiNodeKind.Text && (string.IsNullOrEmpty(node.StyleKey)||HasText(node.StyleKey))){string key=string.IsNullOrEmpty(node.StyleKey)?theme.String("components","buttonLabel"):node.StyleKey;var label=widget.Label;label.font=font;float size=theme.TextNumber(key,"size"),line=theme.TextNumber(key,"lineHeight");label.fontSize=size;label.fontStyle=FontStyles.Normal;label.characterSpacing=0;label.enableAutoSizing=false;var rgba=theme.TextColor(key);label.color=new Color32(rgba[0],rgba[1],rgba[2],rgba[3]);bool wrap=theme.TextWrap(key);label.textWrappingMode=wrap?TextWrappingModes.Normal:TextWrappingModes.NoWrap;float natural=font.faceInfo.lineHeight*size/font.faceInfo.pointSize*font.faceInfo.scale;float leading=line-natural;label.lineSpacing=wrap?leading/(size/100):0;label.margin=new Vector4(0,leading/2,0,leading/2);label.alignment=TextAlignmentOptions.TopLeft;} else return false; return true; }
 bool HasText(string key){foreach(string candidate in theme.TextKeys)if(candidate==key)return true;return false;}
 public void Dispose(){if(buttonSoftLight){if(Application.isPlaying)UnityEngine.Object.Destroy(buttonSoftLight);else UnityEngine.Object.DestroyImmediate(buttonSoftLight);buttonSoftLight=null;}}
        static Image ChildImage(RectTransform parent, string name)
        {
            var found = parent.Find(name);
            if (found) return found.GetComponent<Image>();
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            var result = child.GetComponent<Image>();
            result.raycastTarget = false;
            return result;
        }

        void StyleSelectLabel(TMP_Text label)
        {
            label.font=font; label.fontSize=N("select","fontSize");
            label.fontStyle=FontStyles.Normal; label.characterSpacing=0;
            label.color=C("select","foreground"); label.alignment=TextAlignmentOptions.Left;
            label.richText=false; label.textWrappingMode=TextWrappingModes.NoWrap;
        }
        GameObject FocusFrame(RectTransform root)
        {
            return RectangleBorder(root,"Theme focus",N("focus","width"),C("focus","color"),N("focus","inset"));
        }
        static GameObject RectangleBorder(RectTransform root,string name,float weight,Color color,float inset)
        {
            var group=root.Find(name) as RectTransform;
            if(!group){var obj=new GameObject(name,typeof(RectTransform));obj.transform.SetParent(root,false);group=(RectTransform)obj.transform;}
            Stretch(group,inset);
            foreach(var edge in new[]{"Top","Right","Bottom","Left"})
            {
                var line=ChildImage(group,edge);line.color=color;
                var rect=line.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                if(edge=="Top"||edge=="Bottom") {rect.anchorMin=new Vector2(0,edge=="Top"?1:0);rect.anchorMax=new Vector2(1,edge=="Top"?1:0);rect.pivot=new Vector2(.5f,edge=="Top"?1:0);rect.sizeDelta=new Vector2(0,weight);}
                else {rect.anchorMin=new Vector2(edge=="Right"?1:0,0);rect.anchorMax=new Vector2(edge=="Right"?1:0,1);rect.pivot=new Vector2(edge=="Right"?1:0,.5f);rect.sizeDelta=new Vector2(weight,0);}
                rect.anchoredPosition=Vector2.zero;
            }
            return group.gameObject;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        static void TopRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
 }
}
