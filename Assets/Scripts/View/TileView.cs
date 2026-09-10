using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GridChallenge.Core;

namespace GridChallenge.View
{

    public class TileView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TextMeshProUGUI valueText;

        public int TileId { get; private set; }
        public int Value { get; private set; }
        public bool IsObstacle { get; private set; }
        public Vector2Int GridPosition { get; private set; }

        private RectTransform _rectTransform;
        private Coroutine _moveCoroutine;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (background == null) background = GetComponent<Image>();
            if (valueText == null) valueText = GetComponentInChildren<TextMeshProUGUI>();
        }

        public void Setup(TileData data, Vector2Int pos, Vector2 anchoredPos, Vector2 size)
        {
            TileId = data.Id;
            Value = data.Value;
            IsObstacle = data.IsObstacle;
            GridPosition = pos;

            _rectTransform.sizeDelta = size;
            _rectTransform.anchoredPosition = anchoredPos;
            transform.localScale = Vector3.zero;

            UpdateAppearance();

            StartCoroutine(AnimatePop(Vector3.one, 0.2f));
        }

        public void MoveTo(Vector2 targetAnchoredPos, Vector2Int targetGridPos, float duration, System.Action onComplete = null)
        {
            GridPosition = targetGridPos;
            if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
            _moveCoroutine = StartCoroutine(AnimateMove(targetAnchoredPos, duration, onComplete));
        }

        public void UpdateValue(int newValue)
        {
            Value = newValue;
            UpdateAppearance();
            StartCoroutine(AnimatePunch(1.2f, 0.15f));
        }

        private void UpdateAppearance()
        {
            if (IsObstacle)
            {
                if (background) background.color = new Color(0.28f, 0.28f, 0.32f); // Dark Slate
                if (valueText) valueText.text = "BLOCK";
                if (valueText) valueText.color = Color.white;
                return;
            }

            if (valueText)
            {
                valueText.text = Value > 0 ? Value.ToString() : "";
                valueText.color = Value <= 4 ? new Color(0.47f, 0.43f, 0.40f) : Color.white;
            }

            if (background)
            {
                background.color = GetTileColor(Value);
            }
        }

        private Color GetTileColor(int value)
        {
            return value switch
            {
                2    => new Color(0.93f, 0.89f, 0.85f),
                4    => new Color(0.93f, 0.88f, 0.78f),
                8    => new Color(0.95f, 0.69f, 0.47f),
                16   => new Color(0.96f, 0.58f, 0.39f),
                32   => new Color(0.96f, 0.49f, 0.37f),
                64   => new Color(0.96f, 0.37f, 0.23f),
                128  => new Color(0.93f, 0.81f, 0.45f),
                256  => new Color(0.93f, 0.80f, 0.38f),
                512  => new Color(0.93f, 0.78f, 0.31f),
                1024 => new Color(0.93f, 0.77f, 0.25f),
                2048 => new Color(0.93f, 0.76f, 0.18f),
                _    => new Color(0.24f, 0.24f, 0.24f)
            };
        }

        private IEnumerator AnimateMove(Vector2 targetPos, float duration, System.Action onComplete)
        {
            Vector2 start = _rectTransform.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * (2f - t);
                _rectTransform.anchoredPosition = Vector2.LerpUnclamped(start, targetPos, t);
                yield return null;
            }

            _rectTransform.anchoredPosition = targetPos;
            _moveCoroutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator AnimatePop(Vector3 targetScale, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float scale = Mathf.Sin(t * Mathf.PI * 0.5f);
                transform.localScale = targetScale * scale;
                yield return null;
            }
            transform.localScale = targetScale;
        }

        private IEnumerator AnimatePunch(float punchScale, float duration)
        {
            Vector3 orig = Vector3.one;
            Vector3 peak = orig * punchScale;
            float half = duration * 0.5f;

            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(orig, peak, elapsed / half);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                transform.localScale = Vector3.Lerp(peak, orig, elapsed / half);
                yield return null;
            }

            transform.localScale = orig;
        }
    }
}
