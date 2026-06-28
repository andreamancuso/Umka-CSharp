cmake_minimum_required(VERSION 3.18)
project(umka_shim C)

if(UMKA_ROOT)
    set(UMKA_ROOT "${UMKA_ROOT}" CACHE PATH "Path to the umka-lang checkout")
elseif(DEFINED ENV{UMKA_ROOT})
    set(UMKA_ROOT "$ENV{UMKA_ROOT}" CACHE PATH "Path to the umka-lang checkout")
elseif(WIN32 AND EXISTS "C:/dev/umka-lang/src/umka_api.h")
    set(UMKA_ROOT "C:/dev/umka-lang" CACHE PATH "Path to the umka-lang checkout")
else()
    set(UMKA_ROOT "${CMAKE_SOURCE_DIR}/../external/umka-lang" CACHE PATH "Path to the umka-lang checkout")
endif()

set(UMKA_SRC "${UMKA_ROOT}/src")

if(NOT EXISTS "${UMKA_SRC}/umka_api.h")
    message(FATAL_ERROR "Cannot find Umka sources. Set UMKA_ROOT to a checkout containing src/umka_api.h.")
endif()

set(UMKA_SOURCES
    "${UMKA_SRC}/umka_api.c"
    "${UMKA_SRC}/umka_common.c"
    "${UMKA_SRC}/umka_compiler.c"
    "${UMKA_SRC}/umka_const.c"
    "${UMKA_SRC}/umka_decl.c"
    "${UMKA_SRC}/umka_expr.c"
    "${UMKA_SRC}/umka_gen.c"
    "${UMKA_SRC}/umka_ident.c"
    "${UMKA_SRC}/umka_lexer.c"
    "${UMKA_SRC}/umka_runtime.c"
    "${UMKA_SRC}/umka_stmt.c"
    "${UMKA_SRC}/umka_types.c"
    "${UMKA_SRC}/umka_vm.c"
)

add_library(umka_shim SHARED umka_shim.c ${UMKA_SOURCES})

target_include_directories(umka_shim PRIVATE "${UMKA_SRC}")
target_compile_definitions(umka_shim PRIVATE UMKA_BUILD UMKA_EXT_LIBS)

if(MSVC)
    target_compile_options(umka_shim PRIVATE /W3 /O2)
elseif(WIN32)
    target_compile_options(umka_shim PRIVATE -Wall -Wno-format-security -malign-double -fno-strict-aliasing)
else()
    target_compile_options(umka_shim PRIVATE -fPIC -Wall -Wno-format-security -malign-double -fno-strict-aliasing)
endif()

if(UNIX AND NOT APPLE)
    target_link_libraries(umka_shim PRIVATE m dl)
elseif(UNIX)
    target_link_libraries(umka_shim PRIVATE m)
endif()

install(TARGETS umka_shim
    RUNTIME DESTINATION .
    LIBRARY DESTINATION .
)
