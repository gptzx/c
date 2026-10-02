#include <cstdio>
#include "test_framework.h"

int main(int argc, char** argv) {
    const char* filter = argc > 1 ? argv[1] : nullptr;
    int run = 0;
    for (const auto& t : tf::Registry()) {
        if (filter && std::string(t.name).find(filter) == std::string::npos) continue;
        tf::CurrentTest() = t.name;
        int before = tf::FailCount();
        t.fn();
        ++run;
        std::printf("%s %s\n", tf::FailCount() == before ? "[PASS]" : "[FAIL]", t.name);
    }
    std::printf("\n%d tests run, %d check(s) failed\n", run, tf::FailCount());
    return tf::FailCount() == 0 ? 0 : 1;
}
