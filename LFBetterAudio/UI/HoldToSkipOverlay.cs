using GenUI.Talk;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LFBetterAudio.UI
{
    /// <summary>
    /// 复用原版歌词演出的“长按跳过”控件结构，但使用独立克隆，
    /// 避免修改原版 TalkState 或占用原版歌词流程。
    /// </summary>
    internal sealed class HoldToSkipOverlay
    {
        private const string PromptText = "长按鼠标跳过";

        private GameObject _root;
        private Image _progressImage;
        private NewTalkUI _owner;

        internal void Show(NewTalkUI owner)
        {
            if (owner == null || owner.group_top == null || owner.group_skip_lyrics == null)
            {
                return;
            }

            if (_root == null || !object.ReferenceEquals(_owner, owner))
            {
                Destroy();
                _owner = owner;
                _root = Object.Instantiate(
                    owner.group_skip_lyrics.gameObject,
                    owner.group_top,
                    false);
                _root.name = "BetterAudioHoldToSkip";

                RectTransform rect = _root.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0.5f, 1f);
                    rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.anchoredPosition = new Vector2(0f, -42f);
                }

                _progressImage = FindProgressImage(owner);
                ApplyPromptText();
            }

            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
            SetProgress(0f);
        }

        private Image FindProgressImage(NewTalkUI owner)
        {
            string originalName = owner.img_skip_lyrics == null
                ? "img_skip_lyrics"
                : owner.img_skip_lyrics.name;

            Image[] images = _root.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image != null && image.name == originalName)
                {
                    return image;
                }
            }

            return images.Length > 0 ? images[0] : null;
        }

        private void ApplyPromptText()
        {
            TMP_Text[] tmpTexts = _root.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in tmpTexts)
            {
                if (text != null)
                {
                    text.text = PromptText;
                }
            }

            Text[] legacyTexts = _root.GetComponentsInChildren<Text>(true);
            foreach (Text text in legacyTexts)
            {
                if (text != null)
                {
                    text.text = PromptText;
                }
            }
        }

        internal void SetProgress(float value)
        {
            if (_progressImage != null)
            {
                _progressImage.fillAmount = Mathf.Clamp01(value);
            }
        }

        internal void Hide()
        {
            SetProgress(0f);
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        internal void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
            _root = null;
            _progressImage = null;
            _owner = null;
        }
    }
}
