using System;

namespace AlternativeGameInterface
{
    /// <summary>
    /// Rectangles of a screen in a coarse grid, to answer "does this box overlap any rectangle already placed" without looking at all of them (the map label filter tries up to
    /// a few dozen places for every label; with a few hundred labels the plain loop over the placed boxes was the main cost of a pass). The overlap test is the filter's own
    /// (strict, 1 px margin), the grid only picks the candidates: positions outside the screen go to the border cells, which keeps overlapping boxes in a common cell.
    /// </summary>
    sealed class RectGrid
    {
        const float Cell = 96f;
        int _cw = 1, _ch = 1, _n, _nodes;
        int[] _head = new int[1];
        float[] _x = new float[256], _y = new float[256], _w = new float[256], _h = new float[256];
        int[] _nodeRect = new int[1024], _nodeNext = new int[1024], _seen = new int[256];
        int _stamp;

        public void Reset(float screenW, float screenH)
        {
            _cw = Math.Max(1, (int)Math.Ceiling(screenW / Cell)); _ch = Math.Max(1, (int)Math.Ceiling(screenH / Cell));
            int cells = _cw * _ch;
            if (_head.Length < cells) _head = new int[cells];
            for (int i = 0; i < cells; i++) _head[i] = -1;
            _n = 0; _nodes = 0;
        }

        static int Idx(float v, int max)
        {
            float f = v / Cell;
            if (!(f > 0f)) return 0;
            if (f >= max) return max;
            return (int)f;
        }

        public void Add(float x, float y, float w, float h)
        {
            if (_n == _x.Length)
            {
                int m = _n * 2;
                Array.Resize(ref _x, m); Array.Resize(ref _y, m); Array.Resize(ref _w, m); Array.Resize(ref _h, m); Array.Resize(ref _seen, m);
            }
            int r = _n++;
            _x[r] = x; _y[r] = y; _w[r] = w; _h[r] = h; _seen[r] = 0;
            int x0 = Idx(x, _cw - 1), x1 = Idx(x + w, _cw - 1), y0 = Idx(y, _ch - 1), y1 = Idx(y + h, _ch - 1);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (_nodes == _nodeRect.Length) { Array.Resize(ref _nodeRect, _nodes * 2); Array.Resize(ref _nodeNext, _nodes * 2); }
                    int c = cy * _cw + cx;
                    _nodeRect[_nodes] = r; _nodeNext[_nodes] = _head[c]; _head[c] = _nodes++;
                }
        }

        /// <summary>True when the box overlaps a rectangle that was added (the same strict test with a 1 px margin as the filter's).</summary>
        public bool Hit(float x, float y, float w, float h)
        {
            if (_n == 0) return false;
            int x0 = Idx(x, _cw - 1), x1 = Idx(x + w, _cw - 1), y0 = Idx(y, _ch - 1), y1 = Idx(y + h, _ch - 1);
            if (++_stamp == int.MaxValue) { _stamp = 1; Array.Clear(_seen, 0, _seen.Length); }
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                    for (int k = _head[cy * _cw + cx]; k >= 0; k = _nodeNext[k])
                    {
                        int r = _nodeRect[k];
                        if (_seen[r] == _stamp) continue;
                        _seen[r] = _stamp;
                        if (x + 1f < _x[r] + _w[r] && _x[r] + 1f < x + w && y + 1f < _y[r] + _h[r] && _y[r] + 1f < y + h) return true;
                    }
            return false;
        }
    }
}
