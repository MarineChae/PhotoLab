#include <png.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>

#ifdef _WIN32
#define PH_EXPORT __declspec(dllexport)
#else
#define PH_EXPORT __attribute__((visibility("default")))
#endif

typedef int (*CancelCheck)(void);
typedef struct
{
    png_structp _png;
    png_infop _info;
    const uint8_t* _bytes;
    size_t _size, _offset;
    uint16_t* _pixels;
    int _status;
} DecodeState;

static void ReadBytes(png_structp png, png_bytep output, png_size_t count)
{
    DecodeState* state = (DecodeState*)png_get_io_ptr(png);
    if (count > state->_size - state->_offset) png_error(png, "Truncated PNG.");
    memcpy(output, state->_bytes + state->_offset, count);
    state->_offset += count;
}

static void Error(png_structp png, png_const_charp message)
{
    (void)message;
    png_longjmp(png, 1); // Longjmp stays entirely in C; never crosses a managed frame.
}

static void Warning(png_structp png, png_const_charp message) { (void)png; (void)message; }

PH_EXPORT int ph_png_api_version(void) { return 1; }
PH_EXPORT const char* ph_png_version(void) { return PNG_LIBPNG_VER_STRING; }
PH_EXPORT void ph_png_free(void* pixels) { free(pixels); }

// Return native-endian encoded RGBA16 with straight alpha. No gamma, ICC or premultiplication here.
// Status: 0 success, 1 invalid input, 2 allocation failure, 3 canceled, 4 not 16-bit.
PH_EXPORT int ph_png_decode(const uint8_t* bytes, size_t size, CancelCheck cancel,
    void** pixels, int* width, int* height)
{
    *pixels = NULL; *width = *height = 0;
    if (!bytes || size < 8) return 1;
    DecodeState* state = (DecodeState*)calloc(1, sizeof(DecodeState));
    if (!state) return 2;
    state->_bytes = bytes; state->_size = size; state->_status = 1;
    state->_png = png_create_read_struct(PNG_LIBPNG_VER_STRING, NULL, Error, Warning);
    if (!state->_png) { free(state); return 2; }
    state->_info = png_create_info_struct(state->_png);
    if (!state->_info) { png_destroy_read_struct(&state->_png, NULL, NULL); free(state); return 2; }
    // All mutable cleanup state lives on the heap so it remains valid after longjmp.
    if (setjmp(png_jmpbuf(state->_png))) goto cleanup;
    png_set_read_fn(state->_png, state, ReadBytes);
    png_set_crc_action(state->_png, PNG_CRC_ERROR_QUIT, PNG_CRC_ERROR_QUIT);
    png_read_info(state->_png, state->_info);
    if (png_get_bit_depth(state->_png, state->_info) != 16) { state->_status = 4; goto cleanup; }
    png_uint_32 w = png_get_image_width(state->_png, state->_info);
    png_uint_32 h = png_get_image_height(state->_png, state->_info);
    if (!w || !h || (uint64_t)w * h > INT_MAX / 4 || (uint64_t)w * h > SIZE_MAX / 8) goto cleanup;
    int color = png_get_color_type(state->_png, state->_info);
    int transparent = png_get_valid(state->_png, state->_info, PNG_INFO_tRNS) != 0;
    if (transparent) png_set_tRNS_to_alpha(state->_png);
    if (color == PNG_COLOR_TYPE_GRAY || color == PNG_COLOR_TYPE_GRAY_ALPHA) png_set_gray_to_rgb(state->_png);
    if (!(color & PNG_COLOR_MASK_ALPHA) && !transparent) png_set_add_alpha(state->_png, 65535, PNG_FILLER_AFTER);
    uint16_t endian = 1;
    if (*(uint8_t*)&endian) png_set_swap(state->_png);
    int passes = png_set_interlace_handling(state->_png);
    png_read_update_info(state->_png, state->_info);
    if (png_get_rowbytes(state->_png, state->_info) != (size_t)w * 8) goto cleanup;
    // Adam7 merges partial rows into this initialized full image.
    state->_pixels = (uint16_t*)calloc((size_t)w * h, 8);
    if (!state->_pixels) { state->_status = 2; goto cleanup; }
    for (int pass = 0; pass < passes; pass++)
        for (png_uint_32 row = 0; row < h; row++)
        {
            if (cancel && cancel()) { state->_status = 3; goto cleanup; }
            png_read_row(state->_png, (png_bytep)(state->_pixels + (size_t)row * w * 4), NULL);
        }
    png_read_end(state->_png, state->_info);
    *pixels = state->_pixels; *width = (int)w; *height = (int)h;
    state->_pixels = NULL; state->_status = 0;
cleanup:
    png_destroy_read_struct(&state->_png, &state->_info, NULL);
    free(state->_pixels);
    int status = state->_status; free(state);
    return status;
}

typedef struct
{
    png_structp _png;
    png_infop _info;
    uint8_t* _bytes;
    size_t _size, _capacity;
    int _status;
} EncodeState;

static void WriteBytes(png_structp png, png_bytep bytes, png_size_t count)
{
    EncodeState* state = (EncodeState*)png_get_io_ptr(png);
    if (count > INT_MAX - state->_size) png_error(png, "PNG output is too large.");
    size_t needed = state->_size + count;
    if (needed > state->_capacity)
    {
        size_t capacity = needed > state->_capacity * 2 ? needed : state->_capacity * 2;
        if (capacity > INT_MAX) capacity = INT_MAX;
        uint8_t* expanded = (uint8_t*)realloc(state->_bytes, capacity);
        if (!expanded) { state->_status = 2; png_error(png, "Cannot allocate PNG output."); }
        state->_bytes = expanded; state->_capacity = capacity;
    }
    memcpy(state->_bytes + state->_size, bytes, count);
    state->_size = needed;
}

// Additive ABI v1 entry: source-encoded, native-endian RGBA16 to sRGB-declared PNG bytes.
PH_EXPORT int ph_png_encode(const uint16_t* pixels, int width, int height, CancelCheck cancel,
    void** bytes, uint32_t* size)
{
    *bytes = NULL; *size = 0;
    if (!pixels || width <= 0 || height <= 0 || (uint64_t)width * height > INT_MAX / 4 ||
        (uint64_t)width * height > SIZE_MAX / 8) return 1;
    EncodeState* state = (EncodeState*)calloc(1, sizeof(EncodeState));
    if (!state) return 2;
    state->_status = 1;
    state->_png = png_create_write_struct(PNG_LIBPNG_VER_STRING, NULL, Error, Warning);
    if (!state->_png) { free(state); return 2; }
    state->_info = png_create_info_struct(state->_png);
    if (!state->_info) { png_destroy_write_struct(&state->_png, NULL); free(state); return 2; }
    if (setjmp(png_jmpbuf(state->_png))) goto cleanup;
    png_set_write_fn(state->_png, state, WriteBytes, NULL);
    png_set_IHDR(state->_png, state->_info, (png_uint_32)width, (png_uint_32)height, 16,
        PNG_COLOR_TYPE_RGBA, PNG_INTERLACE_NONE, PNG_COMPRESSION_TYPE_DEFAULT, PNG_FILTER_TYPE_DEFAULT);
    png_set_sRGB_gAMA_and_cHRM(state->_png, state->_info, PNG_sRGB_INTENT_RELATIVE);
    png_write_info(state->_png, state->_info);
    uint16_t endian = 1;
    if (*(uint8_t*)&endian) png_set_swap(state->_png);
    for (int row = 0; row < height; row++)
    {
        if (cancel && cancel()) { state->_status = 3; goto cleanup; }
        png_write_row(state->_png, (png_const_bytep)(pixels + (size_t)row * width * 4));
    }
    png_write_end(state->_png, state->_info);
    *bytes = state->_bytes; *size = (uint32_t)state->_size;
    state->_bytes = NULL; state->_status = 0;
cleanup:
    png_destroy_write_struct(&state->_png, &state->_info);
    free(state->_bytes);
    int status = state->_status; free(state);
    return status;
}

typedef int (*ReadRow)(int row, void* pixels);
typedef int (*WriteChunk)(const void* bytes, size_t size);
typedef struct
{
    png_structp _png;
    png_infop _info;
    uint16_t* _row;
    WriteChunk _write;
    int _status;
} StreamState;

static void StreamBytes(png_structp png, png_bytep bytes, png_size_t size)
{
    StreamState* state = (StreamState*)png_get_io_ptr(png);
    // The managed callback catches exceptions and returns first; longjmp never crosses it.
    if (!state->_write(bytes, size)) png_error(png, "Cannot write PNG stream.");
}

// Additive ABI v1 entry: one borrowed RGBA16 row and immediate output chunks, no full image/result allocation.
PH_EXPORT int ph_png_write(int width, int height, ReadRow read, WriteChunk write, CancelCheck cancel)
{
    if (!read || !write || width <= 0 || height <= 0 || (uint64_t)width * height > INT_MAX / 4 ||
        (uint64_t)width > SIZE_MAX / 8) return 1;
    StreamState* state = (StreamState*)calloc(1, sizeof(StreamState));
    if (!state) return 2;
    state->_status = 1; state->_write = write;
    state->_png = png_create_write_struct(PNG_LIBPNG_VER_STRING, NULL, Error, Warning);
    if (!state->_png) { free(state); return 2; }
    state->_info = png_create_info_struct(state->_png);
    if (!state->_info) { png_destroy_write_struct(&state->_png, NULL); free(state); return 2; }
    if (setjmp(png_jmpbuf(state->_png))) goto cleanup;
    state->_row = (uint16_t*)malloc((size_t)width * 8);
    if (!state->_row) { state->_status = 2; goto cleanup; }
    png_set_write_fn(state->_png, state, StreamBytes, NULL);
    png_set_IHDR(state->_png, state->_info, (png_uint_32)width, (png_uint_32)height, 16,
        PNG_COLOR_TYPE_RGBA, PNG_INTERLACE_NONE, PNG_COMPRESSION_TYPE_DEFAULT, PNG_FILTER_TYPE_DEFAULT);
    png_set_sRGB_gAMA_and_cHRM(state->_png, state->_info, PNG_sRGB_INTENT_RELATIVE);
    png_write_info(state->_png, state->_info);
    uint16_t endian = 1;
    if (*(uint8_t*)&endian) png_set_swap(state->_png);
    for (int row = 0; row < height; row++)
    {
        if (cancel && cancel()) { state->_status = 3; goto cleanup; }
        if (!read(row, state->_row)) goto cleanup;
        png_write_row(state->_png, (png_const_bytep)state->_row);
    }
    png_write_end(state->_png, state->_info);
    state->_status = 0;
cleanup:
    png_destroy_write_struct(&state->_png, &state->_info);
    free(state->_row);
    int status = state->_status; free(state);
    return status;
}
