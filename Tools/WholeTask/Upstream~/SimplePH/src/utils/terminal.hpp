#pragma once
#include <iostream>

namespace terminal
{
    inline void clear_lines(int n)
    {
        for (int i = 0; i < n; ++i)
        {
            std::cout << "\033[F"; // cursor up
            std::cout << "\033[K"; // clear line
        }
    }

    inline void flush()
    {
        std::cout << std::flush;
    }
}