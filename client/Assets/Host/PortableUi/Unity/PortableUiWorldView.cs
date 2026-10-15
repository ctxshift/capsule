using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PortableUi.Unity
{
    // Presentation runs after camera motion, with no guest call or layout work.
    [DefaultExecutionOrder(10000)]
    public sealed class PortableUiWorldView : MonoBehaviour
    {
        UiNode node;
        PortableUiWorldRegistry world;
        PortableUiWorldRegistry.Attachment attachment;
        CanvasGroup group;
        RectTransform rect;
        double started, latest;
        uint revision;
        bool hasEffect, retired, expired;
        public bool Shown {get;private set;}
        public void Apply(UiNode value,PortableUiWorldRegistry registry,double now)
        {
            if(!rect)rect=(RectTransform)transform;
            if(!group)group=gameObject.GetComponent<CanvasGroup>();
            if(!group)group=gameObject.AddComponent<CanvasGroup>();
            if(node==null || world!=registry || node.WorldAnchor?.Entity!=value.WorldAnchor?.Entity || node.WorldAnchor?.Attachment!=value.WorldAnchor?.Attachment)
            {retired=false;attachment=value.WorldAnchor==null?null:registry?.Acquire(value.Handle,value.WorldAnchor);}
            now=Monotonic(now);
            if(value.Effect!=null && (!hasEffect || revision!=value.Effect.Revision))
            {started=now;revision=value.Effect.Revision;expired=false;}
            hasEffect=value.Effect!=null;
            if(!hasEffect)expired=false;
            node=value;world=registry;
        }
        double Monotonic(double now)
        { if(!double.IsNaN(now)&&!double.IsInfinity(now))latest=Math.Max(latest,now);return latest; }
        void LateUpdate()=>Present(Time.unscaledTimeAsDouble);
        public void Present(double now)
        {
            if(node==null || !rect)return;
            now=Monotonic(now);
            var position=new Vector2(node.Rect.X/64f,-node.Rect.Y/64f);
            float alpha=1,scale=1;
            bool visible=node.Visible && !retired;
            if(node.Effect!=null)
            {
                var effect=node.Effect;
                double elapsed=Math.Max(0,(now-started)*1000);
                if(elapsed>=effect.DurationMs)expired=true;
                visible &= !expired;
                float t=(float)Math.Min(1,elapsed/effect.DurationMs);
                position+=new Vector2(effect.X,-effect.Y)*(t/64f);
                if(effect.FadeMs>0)alpha=Mathf.Clamp01((float)((effect.DurationMs-elapsed)/effect.FadeMs));
            }
            var anchor=node.WorldAnchor;
            if(visible && anchor!=null)
            {
                if(world==null || attachment==null || !attachment.TryResolve(anchor,out var point))
                {retired=true;visible=false;}
                else
                {
                    var camera=world.Camera;
                    visible=camera && camera.isActiveAndEnabled;
                    if(visible)
                    {
                        var projected=camera.WorldToViewportPoint(point);
                        float distance=Vector3.Distance(camera.transform.position,point);
                        visible=projected.z>=camera.nearClipPlane && projected.z<=camera.farClipPlane &&
                            projected.x>=0 && projected.x<=1 && projected.y>=0 && projected.y<=1 &&
                            distance>=anchor.Near/64f && distance<=anchor.Far/64f;
                        if(visible && anchor.Occlusion)
                        {try{visible=world.Visibility!=null && world.Visibility(point);}catch{visible=false;}}
                        scale=Mathf.Clamp(anchor.ReferenceDistance==0?1:anchor.ReferenceDistance/64f/Mathf.Max(distance,0.0001f),anchor.MinScale/64f,anchor.MaxScale/64f);
                        var parent=(RectTransform)rect.parent;
                        var canvas=GetComponentInParent<Canvas>();
                        var uiCamera=canvas && canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
                        if(!parent || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,camera.WorldToScreenPoint(point),uiCamera,out var local))visible=false;
                        else position+=new Vector2(local.x-parent.rect.xMin,local.y-parent.rect.yMax);
                    }
                }
            }
            rect.anchoredPosition=position;rect.localScale=Vector3.one*scale;
            group.alpha=visible?alpha:0;group.blocksRaycasts=group.interactable=visible && alpha>0;
            Shown=visible && alpha>0;
            if(!Shown && EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
