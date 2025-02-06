using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace com.demo
{
    /// <ExplanationForDemo>
    ///     Example of optimizing the match detection of a match 3 game, using bit masks.
    ///     Part of the fixed design was, no board would ever exceed an 8x8 grid.
    ///
    ///     A two dimensional boolean array should have been already performant enough, but this
    ///     serves as a nice case study.
    /// 
    /// <!ExplanationForDemo>
    
    /// <summary>
    /// A shape encodes token positions in bit wise in an ulong variable as following:
    ///
    ///
    /// The board grid:
    ///   - starts from lower left corner at position 0,0
    ///   - direction from left-to-right, bottom-to-top
    ///
    ///            -------------------------------------------------
    ///         -> | 7,0 | 7,1 | 7,2 | 7,3 | 7,4 | 7,5 | 7,6 | 7,7 |
    ///         -> | 6,0 | 6,1 | 6,2 | 6,3 | 6,4 | 6,5 | 6,6 | 6,7 |
    ///         -> | 5,0 | 5,1 | 5,2 | 5,3 | 5,4 | 5,5 | 5,6 | 5,7 |
    ///         -> | 4,0 | 4,1 | 4,2 | 4,3 | 4,4 | 4,5 | 4,6 | 4,7 |
    ///         -> | 3,0 | 3,1 | 3,2 | 3,3 | 3,4 | 3,5 | 3,6 | 3,7 |
    ///         -> | 2,0 | 2,1 | 2,2 | 2,3 | 2,4 | 2,5 | 2,6 | 2,7 |
    ///         -> | 1,0 | 1,1 | 1,2 | 1,3 | 1,4 | 1,5 | 1,6 | 1,7 | 
    ///         -> | 0,0 | 0,1 | 0,2 | 0,3 | 0,4 | 0,5 | 0,6 | 0,7 |
    ///            ------------------------------------------------- 
    ///
    ///
    /// The ulong:
    ///   - encodes position 0,0 as the least significant bit
    ///   - direction from right to left
    ///
    ///            | 7,7 | .. | .. | .. | 0,7 | 0,6 | 0,5 | 0,4 | 0,3 | 0,2 | 0,1 | 0,0 |
    ///
    /// </summary>
    public struct Shape
    {
        public static int MaxShapeSizeX = 8;
        public static int MaxShapeSizeY = 8;
        public int PosX => posX;
        public int PosY => posY;
        public int Width => width;
        public int Height => height;
        public ulong BitField => bitField;
        private ulong bitField;
        private int maxX;
        private int maxY;
        private int posX;
        private int posY;
        private int width;
        private int height;
        
        public Shape(List<Vector2Int> tokenPositions) 
        {
            posX = 8;
            posY = 8;
            this.bitField = 0;
            
            foreach (var tokenPos in tokenPositions)
            {
                var bitMask = GetBitMask(tokenPos.x, tokenPos.y);
                bitField |= bitMask;
            }
            
            CalculateBoundingBox();
        }

        private Shape(ulong bitField)
        {
            posX = 8;
            posY = 8;
            this.bitField = bitField;
            CalculateBoundingBox();
        }

        public static Shape Add(this Shape shape, Shape other)
        {
            return new Shape(shape.BitField | other.BitField);
            
        }
        public static Shape Substract(this Shape shape,Shape other)
        {
            return new Shape(shape.BitField & ~other.BitField);
        }

        public static Shape Intersect(this Shape shape, Shape other)
        {
            return new Shape(shape.BitField & other.BitField);
        }

        public static Shape Invert(this Shape shape)
        {
            return new Shape(~shape.BitField);
        }

        /// <summary>
        /// Tries to move this shape on x and y until it "fits" in the other shape
        /// </summary>
        /// <returns>true, if it matches</returns>
        public bool DoesMatch(Shape other)
        {
            var cachedBitField = bitField;
            (int posX,int maxX, int posY,int maxY) cachedBoundingBox = (posX, maxX, posY, maxY);
            
            for (var x = other.PosX; x <= MaxShapeSizeX - width; ++x)
                for (var y = other.PosY; y <= MaxShapeSizeY - height; ++y)
                {
                    MoveTo(x, y);
                    if (other.Contains(this))
                    {
                        RestoreValues(cachedBoundingBox, cachedBitField);
                        return true;
                    }
                }
            RestoreValues(cachedBoundingBox, cachedBitField);
            return false;
        }

        private void RestoreValues((int posX,int maxX, int posY,int maxY) boundingBox, ulong cachedBitField)
        {
            bitField = cachedBitField;
            posX = boundingBox.posX;
            maxX = boundingBox.maxX;      
            posY = boundingBox.posY;
            maxY = boundingBox.maxY;
        }

        public void MoveTo(int x, int y)
        {
            var horizontal = x - posX;
            var vertical = (y - posY) * MaxShapeSizeX;

            ShiftBits(horizontal + vertical);

            posX = x;
            posY = y;
            maxX = x + width - 1;
            maxY = y + height - 1;
        }
        private void ShiftBits(int amount)
        {
            if (amount > 0)
                bitField <<= amount;
            else if (amount < 0)
                bitField >>= -amount;
        }
        public bool Contains(Shape other)
        {
            return (bitField & other.BitField) == other.BitField;
        }

        public int CountSetBits()
        {
            var count = 0;
            var temp = bitField;
            while (temp > 0)
            {
                temp &= temp - 1; // clear the least significant bit set
                count++;
            }

            return count;
        }
        
        public static bool operator ==(Shape a, Shape b)
        {
            return a.BitField == b.BitField;
        }

        public static bool operator !=(Shape a, Shape b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            return obj is Shape other && this == other;
        }

        private bool CanMove(int x, int y)
        {
            return x >= 0 && x + width <= MaxShapeSizeX &&
                   y >= 0 && y + height <= MaxShapeSizeY;
        }

        private void CalculateBoundingBox()
        {
            if (bitField == 0)
            {
                posX = posY = -1;
                width = height = 0;
                maxX = maxY = 8;
                return;
            }

            maxX = 0;
            maxY = 0;
            posX = MaxShapeSizeX - 1;
            posY = MaxShapeSizeY - 1;

            var temp = bitField;

            for (var i = 0; i < 64 && temp > 0; ++i)
            {
                if ((temp & 1) == 1)
                {
                    var x = i % MaxShapeSizeX;

                    if (x > maxX)
                        maxX = x;
                    if (x < posX)
                        posX = x;

                    var y = i / MaxShapeSizeX;
                    if (y > maxY)
                        maxY = y;
                    if (y < posY)
                        posY = y;
                }

                temp >>= 1;
            }

            width = maxX - posX + 1;
            height = maxY - posY + 1;
        }

        public bool IsBitSet(int x, int y)
        {
            var bitMask = GetBitMask(x, y);
            return bitField != 0 && (bitField & bitMask) == bitMask;
        }

        private ulong GetBitMask(int x, int y)
        {
            return (ulong) 1 << x << (MaxShapeSizeX * y);
        }
        
        public List<Vector2Int> ConvertToCoordinates()
        {
            var tokenPositions = new List<Vector2Int>();
            var temp = bitField;
            for (var i = 0; i < 64 && temp > 0; ++i)
            {
                if ((temp & 1) == 1)
                {
                    var x = i % MaxShapeSizeX;
                    var y = i / MaxShapeSizeX;

                    tokenPositions.Add(new Vector2Int(x, y));
                }

                temp >>= 1;
            }

            return tokenPositions;
        }
    }
}