// 役割: 型安全なグローバルイベントバス。
//       各システムを疎結合に繋ぐ Core 基盤インフラ。
//       Subscribe / Unsubscribe / Publish の3操作のみを提供する。
//       イベント型ごとに独立したデリゲートチェーンを保持する。

using System;
using System.Collections.Generic;

namespace ElfVillage.Core
{
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> _handlers = new();

        public static void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            _handlers[type] = _handlers.TryGetValue(type, out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var existing)) return;
            var updated = Delegate.Remove(existing, handler);
            if (updated == null) _handlers.Remove(type);
            else                 _handlers[type] = updated;
        }

        public static void Publish<T>(T evt)
        {
            if (_handlers.TryGetValue(typeof(T), out var handler))
                ((Action<T>)handler)?.Invoke(evt);
        }
    }
}
