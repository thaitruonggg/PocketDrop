import os
import random

# ==========================================
# 1. CATEGORY & WEIGHT DEFINITIONS
# ==========================================

CATEGORIES = {
    "Heavy": (
        ["mp4", "3gp", "avi", "mpg", "mov", "wmv", "rar", "zip", "hqx", "arj", "tar", "arc", "sit", "gz", "z"],
        500 * 1024 * 1024,      # 500 MB
        3 * 1024 * 1024 * 1024, # 3 GB
        5                       # ~5 files each
    ),
    "Medium": (
        ["jpg", "png", "webp", "gif", "tif", "bmp", "eps", "mp3", "wma", "snd", "wav", "ra", "au", "aac", "exe", "msi"],
        10 * 1024 * 1024,       # 10 MB
        100 * 1024 * 1024,      # 100 MB
        20                      # ~20 files each
    ),
    "Light": (
        ["txt", "rtf", "docx", "csv", "doc", "wps", "wpd", "msg", "c", "ccp", "java", "py", "js", "ts", "cs", "swift", "dta", "pl", "sh", "bat", "com", "html", "htm", "xhtml", "asp", "css", "aspx", "rss"],
        10 * 1024,              # 10 KB
        5 * 1024 * 1024         # 5 MB
        50                      # ~50 files each
    )
}

# ==========================================
# 2. GENERATOR LOGIC
# ==========================================

# ✨ THE FIX: Hardcoded to the D:\ drive using a raw string
BASE_DIR = r"D:\PocketDrop_100GB_Test"
CHUNK_SIZE = 100 * 1024 * 1024  

def create_true_file(filepath, target_size):
    with open(filepath, "wb") as f:
        bytes_written = 0
        while bytes_written < target_size:
            write_size = min(CHUNK_SIZE, target_size - bytes_written)
            f.write(os.urandom(write_size))
            bytes_written += write_size

def generate_environment():
    os.makedirs(BASE_DIR, exist_ok=True)
    
    for i in range(5):
        os.makedirs(os.path.join(BASE_DIR, f"Empty_Folder_{i}"), exist_ok=True)
        
    for i in range(5):
        with open(os.path.join(BASE_DIR, f"link_{i}.url"), "w") as f:
            f.write("[InternetShortcut]\nURL=https://github.com/naofunyan/PocketDrop\n")

    total_bytes_expected = 0
    file_count = 0

    print(f"Starting the 100 GB True-Data Generation in {BASE_DIR}...")
    print("WARNING: This will take significant time depending on your SSD write speed.\n")

    for weight_class, (extensions, min_size, max_size, count) in CATEGORIES.items():
        print(f"--- Generating {weight_class}weights ---")
        
        class_dir = os.path.join(BASE_DIR, f"{weight_class}_Contents")
        os.makedirs(class_dir, exist_ok=True)

        for ext in extensions:
            for i in range(count):
                file_count += 1
                size = random.randint(min_size, max_size)
                total_bytes_expected += size
                
                target_dir = class_dir if random.choice([True, False]) else BASE_DIR
                filepath = os.path.join(target_dir, f"test_{file_count:04d}.{ext}")
                
                print(f"Writing {filepath} ({(size / (1024*1024)):.1f} MB)...")
                create_true_file(filepath, size)

    print("\n==========================================")
    print(f"DONE! Generated {file_count} files.")
    print(f"Total True Data Written: {(total_bytes_expected / (1024**3)):.2f} GB")
    print(f"Files saved to: {BASE_DIR}")
    print("==========================================")

if __name__ == "__main__":
    generate_environment()