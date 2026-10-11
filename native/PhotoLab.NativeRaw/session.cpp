#include <libraw/libraw.h>
#include <cstdint>
#include <memory>
#include <new>
#ifdef _WIN32
#include <windows.h>
#define PH_EXPORT extern "C" __declspec(dllexport)
#else
#define PH_EXPORT extern "C" __attribute__((visibility("default")))
#endif

using CancelCheck = int (*)();

// Compiled against LibRaw headers: no version-dependent struct layout crosses the C ABI.
struct RawSession
{
    LibRaw _raw;
    CancelCheck _cancel = nullptr;
    bool _valid = true;
    static int Progress(void* data, enum LibRaw_progress, int, int)
    {
        auto* session = static_cast<RawSession*>(data);
        return session->_cancel ? session->_cancel() : 0;
    }
};

struct RestoreActiveSize
{
    libraw_image_sizes_t& _sizes;
    unsigned short _width, _height;
    explicit RestoreActiveSize(libraw_image_sizes_t& sizes) : _sizes(sizes), _width(sizes.width), _height(sizes.height) {}
    ~RestoreActiveSize() { _sizes.width = _width; _sizes.height = _height; }
};

PH_EXPORT int ph_raw_api_version() { return 1; }
PH_EXPORT const char* ph_raw_version() { return LibRaw::version(); }
PH_EXPORT const char* ph_raw_error(int status) { return LibRaw::strerror(status); }

PH_EXPORT void* ph_raw_open(const char* path, CancelCheck cancel, int* status) noexcept
{
    *status = LIBRAW_UNSUFFICIENT_MEMORY;
    try
    {
        // Public C++ object layout must match the dynamically loaded LibRaw implementation.
        if (LibRaw::versionNumber() != LIBRAW_VERSION)
        { *status = LIBRAW_NOT_IMPLEMENTED; return nullptr; }
        auto session = std::make_unique<RawSession>();
        auto& options = session->_raw.imgdata.params;
        options.use_camera_wb = 0;
        options.use_auto_wb = 0;
        options.use_camera_matrix = 3; // Same as +M in the explicit-WB CLI path.
        options.output_color = 1;
        options.output_bps = 16;
        options.no_auto_bright = 1;
        options.gamm[0] = options.gamm[1] = 1;
        session->_cancel = cancel;
        session->_raw.set_progress_handler(RawSession::Progress, session.get());
#ifdef _WIN32
        int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, nullptr, 0);
        if (!length) { *status = LIBRAW_IO_ERROR; return nullptr; }
        auto wide = std::make_unique<wchar_t[]>(length);
        MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, wide.get(), length);
        *status = session->_raw.open_file(wide.get());
#else
        *status = session->_raw.open_file(path);
#endif
        if (*status) return nullptr;
        *status = session->_raw.unpack();
        if (*status) return nullptr;
        // Keep decoded sensor data but release the file stream: repeated development cannot reread the source.
        session->_raw.recycle_datastream();
        session->_cancel = nullptr;
        return session.release();
    }
    catch (...) { *status = LIBRAW_UNSUFFICIENT_MEMORY; return nullptr; } // Never unwind through managed frames.
}

PH_EXPORT int ph_raw_develop(void* handle, float c0, float c1, float c2, float c3, int halfSize, CancelCheck cancel,
    void** image, void** data, int* width, int* height, std::uint32_t* size) noexcept
{
    *image = *data = nullptr;
    *width = *height = 0; *size = 0;
    auto* session = static_cast<RawSession*>(handle);
    if (!session || !session->_valid) return LIBRAW_OUT_OF_ORDER_CALL;
    session->_cancel = cancel;
    try
    {
        auto& options = session->_raw.imgdata.params;
        options.half_size = halfSize ? 1 : 0;
        auto& rawdata = session->_raw.imgdata.rawdata;
        RestoreActiveSize restore(rawdata.sizes);
        // open_datastream trims odd Bayer active dimensions for -h. Mirror that crop without modifying cached pixels.
        if (halfSize && rawdata.iparams.filters >= 1000)
        { rawdata.sizes.width &= 65534; rawdata.sizes.height &= 65534; }
        options.user_mul[0] = c0; options.user_mul[1] = c1;
        options.user_mul[2] = c2; options.user_mul[3] = c3;
        // dcraw_process restores its working image from rawdata on every call; rawdata stays cached.
        int status = session->_raw.dcraw_process();
        if (status)
        { session->_valid = false; session->_cancel = nullptr; return status; }
        auto* output = session->_raw.dcraw_make_mem_image(&status);
        session->_cancel = nullptr;
        if (!output) return status ? status : LIBRAW_UNSUFFICIENT_MEMORY;
        std::uint64_t expected = std::uint64_t(output->width) * output->height * 6;
        if (output->type != LIBRAW_IMAGE_BITMAP || output->bits != 16 || output->colors != 3 ||
            !output->width || !output->height || expected != output->data_size)
        { LibRaw::dcraw_clear_mem(output); return LIBRAW_NOT_IMPLEMENTED; }
        *image = output; *data = output->data;
        *width = output->width; *height = output->height; *size = output->data_size;
        return 0;
    }
    catch (...)
    { session->_valid = false; session->_cancel = nullptr; return LIBRAW_UNSUFFICIENT_MEMORY; }
}

PH_EXPORT void ph_raw_clear_image(void* image) noexcept
{ LibRaw::dcraw_clear_mem(static_cast<libraw_processed_image_t*>(image)); }
PH_EXPORT void ph_raw_close(void* handle) noexcept
{ delete static_cast<RawSession*>(handle); }
