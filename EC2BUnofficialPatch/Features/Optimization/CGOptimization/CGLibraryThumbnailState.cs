using System;
using System.IO;
using EC2BUnofficialPatch.Core;
using GenUI.Main;
using Sdk;
using UnityEngine;

namespace EC2BUnofficialPatch.Features.Optimization.CGOptimization
{
    /// <summary>只管理 CG 图鉴格子的缩略图请求，不改变其他 UISprite 的加载方式。</summary>
    internal sealed class CGLibraryThumbnailState : MonoBehaviour
    {
        private const float LoadTimeoutSeconds = 20f;

        private Cell_CGLibraryItemUI _cell;
        private UISprite _icon;
        private string _entryUrl;
        private string _resourcePath;
        private int _version;
        private bool _hasRequest;
        private bool _completed;
        private float _startedAt;

        internal void BeginRender(Cell_CGLibraryItemUI cell)
        {
            _version++;
            _cell = cell;
            _icon = cell.icon_bg;
            _entryUrl = null;
            _resourcePath = null;
            _hasRequest = false;
            _completed = true;
        }

        internal bool TryLoad(UISprite icon, string url, bool showWhenComplete)
        {
            if (!ReferenceEquals(icon, _icon))
                return false;

            int version = _version;
            _entryUrl = _cell.GetKeyObj<string>("url");
            _hasRequest = true;
            _completed = false;
            _startedAt = Time.realtimeSinceStartup;
            icon.showWhenComp = showWhenComplete;
            icon.image.sprite = null;
            icon.gameObject.SetActive(false);

            try
            {
                bool external;
                string path = ResolvePath(url, out external);
                _resourcePath = path;
                if (string.IsNullOrEmpty(path) || (external && !File.Exists(path)))
                {
                    Complete(version, null, "资源路径不存在");
                    return true;
                }

                if (external)
                    ResMgr.LoadExternSpriteAsync(path, sprite => Complete(version, sprite, null));
                else
                    ResMgr.LoadSpriteAsync(path, sprite => Complete(version, sprite, null));
            }
            catch (Exception exception)
            {
                Complete(version, null, ModuleHost.GetReason(exception));
            }
            return true;
        }

        internal void AfterRender()
        {
            if (_hasRequest && _cell?.root_loading != null)
                _cell.root_loading.gameObject.SetActive(!_completed);
        }

        private static string ResolvePath(string url, out bool external)
        {
            external = false;
            if (string.IsNullOrEmpty(url))
                return null;
            if (Path.IsPathRooted(url))
            {
                external = true;
                return url;
            }
            if (url.StartsWith("Mods", StringComparison.Ordinal))
            {
                external = true;
                return Singleton<ModCtrl>.Ins.GetFullUrl(url);
            }
            return Path.Combine("Textures/", LocalizationMgr.GetLocalizeUrl(url));
        }

        private void Complete(int version, Sprite sprite, string error)
        {
            if (version != _version || _completed || _cell == null || !_cell.isShow ||
                !string.Equals(_entryUrl, _cell.GetKeyObj<string>("url"), StringComparison.Ordinal))
                return;

            _completed = true;
            if (sprite != null)
                _icon.SetSprite(sprite);
            else
                PatchLog.Warning(
                    $"优化模块-CG图鉴缩略图加载失败：url={_entryUrl}, path={_resourcePath}, " +
                    $"reason={error ?? "返回空贴图"}");

            _cell.root_loading?.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_resourcePath != null && !_completed &&
                Time.realtimeSinceStartup - _startedAt >= LoadTimeoutSeconds)
                Complete(_version, null, "加载超时");
        }

        private void OnDisable()
        {
            _version++;
            _hasRequest = false;
            _completed = true;
            _resourcePath = null;
        }
    }
}
