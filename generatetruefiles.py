import os
import random

# ==========================================
# 1. CATEGORIES, FORMATS & 70 GB SIZES
# ==========================================

CATEGORIES = {
    "Video Formats": (
        ["mp4", "3gp", "avi", "mpg", "mov", "wmv"],
        5 * 1024 * 1024,        # 5 MB
        45 * 1024 * 1024        # 45 MB
    ),
    "Compressed Formats": (
        ["rar", "zip", "hqx", "arj", "tar", "arc", "sit", "gz", "z"],
        5 * 1024 * 1024,        # 5 MB
        45 * 1024 * 1024        # 45 MB
    ),
    "Image Formats": (
        ["jpg", "png", "webp", "gif", "tif", "bmp", "eps"],
        1 * 1024 * 1024,        # 1 MB
        5 * 1024 * 1024         # 5 MB
    ),
    "Audio Formats": (
        ["mp3", "wma", "snd", "wav", "ra", "au", "aac"],
        1 * 1024 * 1024,        # 1 MB
        5 * 1024 * 1024         # 5 MB
    ),
    "Text Formats": (
        ["txt", "rtf", "docx", "csv", "doc", "wps", "wpd", "msg"],
        10 * 1024,              # 10 KB
        500 * 1024              # 500 KB
    ),
    "Program Formats": (
        ["c", "ccp", "java", "py", "js", "ts", "cs", "swift", "dta", "pl", "sh", "bat", "com", "exe", "msi"],
        10 * 1024,              # 10 KB
        500 * 1024              # 500 KB
    ),
    "Web Formats": (
        ["html", "htm", "xhtml", "asp", "css", "aspx", "rss"],
        10 * 1024,              # 10 KB
        500 * 1024              # 500 KB
    )
}

TIERS = [1, 10, 50, 100]
BASE_DIR = r"D:\PocketDrop_Master_Test_Suite"
CHUNK_SIZE = 10 * 1024 * 1024  # Bumped to 10 MB chunks to speed up the heavy writes

# ==========================================
# 2. GENERATION LOGIC
# ==========================================

def create_true_file(filepath, target_size):
    """Writes actual random bytes to the file in safe chunks."""
    with open(filepath, "wb") as f:
        bytes_written = 0
        while bytes_written < target_size:
            write_size = min(CHUNK_SIZE, target_size - bytes_written)
            f.write(os.urandom(write_size))
            bytes_written += write_size

print(f"Starting Unified True Data Generation in {BASE_DIR}...")
print("WARNING: Writing ~70 GB of real random data will take a significant amount of time!\n")

total_files_created = 0
total_bytes = 0

for category_name, (extensions, min_size, max_size) in CATEGORIES.items():
    print(f"---> Processing {category_name}...")
    
    for ext in extensions:
        for tier in TIERS:
            dir_path = os.path.join(BASE_DIR, category_name, ext, str(tier))
            os.makedirs(dir_path, exist_ok=True)
            
            for i in range(1, tier + 1):
                size = random.randint(min_size, max_size)
                total_bytes += size
                
                filename = os.path.join(dir_path, f"test_{ext}_{i:03d}.{ext}")
                
                create_true_file(filename, size)
                total_files_created += 1

print("\n==========================================")
print(f"DONE! Successfully generated and organized {total_files_created} TRUE DATA files.")
print(f"Total Real Data Written: {(total_bytes / (1024**3)):.2f} GB")
print(f"Your structured test library is ready at: {BASE_DIR}")
print("==========================================")