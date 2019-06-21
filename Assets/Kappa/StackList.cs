using System;

namespace Kappa
{
    public struct StackList<T>
    {
        public const byte MaxElementCount = 16;

        private T _mElement0;
        private T _mElement1;
        private T _mElement2;
        private T _mElement3;
        private T _mElement4;
        private T _mElement5;
        private T _mElement6;
        private T _mElement7;

        private T _mElement8;
        private T _mElement9;
        private T _mElement10;
        private T _mElement11;
        private T _mElement12;
        private T _mElement13;
        private T _mElement14;
        private T _mElement15;

        private int _mInternalIndex;


        public T this[int index]
        {
            get
            {
                if (index < 8)
                {
                    if (index == 0) return _mElement0;
                    else if (index == 1) return _mElement1;
                    else if (index == 2) return _mElement2;
                    else if (index == 3) return _mElement3;
                    else if (index == 4) return _mElement4;
                    else if (index == 5) return _mElement5;
                    else if (index == 6) return _mElement6;
                    else return _mElement7;
                }
                else
                {
                    if (index == 8) return _mElement8;
                    else if (index == 9) return _mElement9;
                    else if (index == 10) return _mElement10;
                    else if (index == 11) return _mElement11;
                    else if (index == 12) return _mElement12;
                    else if (index == 13) return _mElement13;
                    else if (index == 14) return _mElement14;
                    else return _mElement15;
                }
            }

            set
            {
                if (index < 8)
                {
                    if (index == 0) _mElement0 = value;
                    else if (index == 1) _mElement1 = value;
                    else if (index == 2) _mElement2 = value;
                    else if (index == 3) _mElement3 = value;
                    else if (index == 4) _mElement4 = value;
                    else if (index == 5) _mElement5 = value;
                    else if (index == 6) _mElement6 = value;
                    else _mElement7 = value;
                }
                else
                {
                    if (index == 8) _mElement8 = value;
                    else if (index == 9) _mElement9 = value;
                    else if (index == 10) _mElement10 = value;
                    else if (index == 11) _mElement11 = value;
                    else if (index == 12) _mElement12 = value;
                    else if (index == 13) _mElement13 = value;
                    else if (index == 14) _mElement14 = value;
                    else _mElement15 = value;
                }
            }
        }

        public int Count { get { return _mInternalIndex; } }

        public bool Add(T element)
        {
            if (_mInternalIndex == MaxElementCount) return false;

            this[_mInternalIndex] = element;
            _mInternalIndex++;

            return true;
        }

        public bool Remove(T element)
        {
            return Remove(element, DefaultComparison);
        }

        public bool Remove(T element, Func<T, T, bool> comparison)
        {
            var removeIndex = -1;
            for (int i = 0; i < MaxElementCount; i++)
            {
                if (comparison(this[i], element))
                {
                    removeIndex = i;
                    break;
                }
            }

            if (removeIndex > -1)
            {
                RemoveAt(removeIndex);
            }

            return removeIndex > -1;
        }

        public void RemoveAt(int elementIndex)
        {
            for (int i = elementIndex; i < MaxElementCount - 1; i++)
            {
                this[i] = this[i + 1];
            }

            _mInternalIndex--;
        }

        private bool DefaultComparison(T x, T y)
        {
            return x.Equals(y);
        }
    }
}
