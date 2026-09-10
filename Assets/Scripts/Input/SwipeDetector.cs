using System;
using UnityEngine;
using UnityEngine.EventSystems;
using GridChallenge.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GridChallenge.Input
{

    public class SwipeDetector : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public event Action<Direction> OnSwipe;

        [Header("Sensitivity Settings")]
        [SerializeField] private float minSwipeDistance = 40f;
        [SerializeField] private float maxSwipeTime = 1.0f;

        private Vector2 _pointerDownPosition;
        private float _pointerDownTime;
        private bool _isSwiping;

        public void OnPointerDown(PointerEventData eventData)
        {
            _pointerDownPosition = eventData.position;
            _pointerDownTime = Time.unscaledTime;
            _isSwiping = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isSwiping) return;

            Vector2 delta = eventData.position - _pointerDownPosition;
            if (delta.magnitude >= minSwipeDistance)
            {
                ResolveSwipe(delta);
                _isSwiping = false;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_isSwiping) return;
            _isSwiping = false;

            float duration = Time.unscaledTime - _pointerDownTime;
            if (duration > maxSwipeTime) return;

            Vector2 delta = eventData.position - _pointerDownPosition;
            if (delta.magnitude >= minSwipeDistance)
            {
                ResolveSwipe(delta);
            }
        }

        private void ResolveSwipe(Vector2 delta)
        {
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            {
                OnSwipe?.Invoke(delta.x > 0 ? Direction.Right : Direction.Left);
            }
            else
            {
                OnSwipe?.Invoke(delta.y > 0 ? Direction.Up : Direction.Down);
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)    OnSwipe?.Invoke(Direction.Up);
                else if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)  OnSwipe?.Invoke(Direction.Down);
                else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)  OnSwipe?.Invoke(Direction.Left);
                else if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) OnSwipe?.Invoke(Direction.Right);
            }
#else
            if (UnityEngine.Input.GetKeyDown(KeyCode.W) || UnityEngine.Input.GetKeyDown(KeyCode.UpArrow))    OnSwipe?.Invoke(Direction.Up);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.S) || UnityEngine.Input.GetKeyDown(KeyCode.DownArrow))  OnSwipe?.Invoke(Direction.Down);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.A) || UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow))  OnSwipe?.Invoke(Direction.Left);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.D) || UnityEngine.Input.GetKeyDown(KeyCode.RightArrow)) OnSwipe?.Invoke(Direction.Right);
#endif
        }
    }
}
