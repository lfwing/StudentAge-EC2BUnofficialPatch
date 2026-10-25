using System.Collections.Generic;
using HarmonyLib;
using Sdk;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using View.Hint;

namespace EC2BUnofficialPatch.Features.Effects
{
    // A separate instance of the game's confirm prefab keeps its paper, typography,
    // buttons, mask and animations, without changing ordinary confirmation dialogs.
    internal sealed class TraitDescriptionView : CommonComfirmView
    {
        internal TraitCardContent Owner { get; private set; }
        private ScrollRect scroll;

        internal static TraitDescriptionView Show(TraitCardContent owner, string title, string content)
        {
            var view = UIMgr.GetView(typeof(TraitDescriptionView).FullName) as TraitDescriptionView;
            if (view == null)
            {
                view = new TraitDescriptionView();
                object manager = AccessTools.Field(typeof(UIMgr), "ins").GetValue(null);
                var views = (Dictionary<string, BaseView>)AccessTools.Field(typeof(UIMgr), "viewDict").GetValue(manager);
                views.Add(typeof(TraitDescriptionView).FullName, view);
            }
            view.Owner = owner;
            UIMgr.OpenView<TraitDescriptionView>(UILayerType.None, null,
                content, null, null, false, title, "关闭", null, "滚轮或拖动查看完整说明", true);
            return view;
        }

        public override void InitUI()
        {
            base.InitUI();
            var body = txtex_desc.rectTransform;
            var viewportObject = new GameObject("UP.TraitDescriptionScroll", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(body.parent, false);
            viewport.SetSiblingIndex(body.GetSiblingIndex());
            viewport.anchorMin = body.anchorMin; viewport.anchorMax = body.anchorMax;
            viewport.pivot = body.pivot; viewport.sizeDelta = body.sizeDelta;
            viewport.anchoredPosition = body.anchoredPosition;
            viewportObject.GetComponent<Image>().color = Color.clear;
            body.SetParent(viewport, false);
            body.anchorMin = new Vector2(0, 1); body.anchorMax = Vector2.one;
            body.pivot = new Vector2(.5f, 1);
            body.offsetMin = body.offsetMax = Vector2.zero;
            txtex_desc.enableAutoSizing = false;
            txtex_desc.enableWordWrapping = true;
            txtex_desc.alignment = TextAlignmentOptions.TopLeft;
            txtex_desc.overflowMode = TextOverflowModes.Overflow;
            txtex_desc.raycastTarget = true;
            var fitter = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = body;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = txtex_desc.fontSize;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(txtex_desc.rectTransform);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }

        public override void OnClose()
        {
            Owner = null;
            base.OnClose();
        }
    }
}
