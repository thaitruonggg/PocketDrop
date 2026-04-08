import os
import random

# ==========================================
# 1. THE 62 FORMATS & SCALED DOWN SIZES (< 30 GB)
# ==========================================

CATEGORIES = {
    "Heavy": (
        ["mp4", "3gp", "avi", "mpg", "mov", "wmv", "rar", "zip", "hqx", "arj", "tar", "arc", "sit", "gz", "z"],
        1 * 1024 * 1024,        # 1 MB
        15 * 1024 * 1024        # 15 MB
    ),
    "Medium": (
        ["jpg", "png", "webp", "gif", "tif", "bmp", "eps", "mp3", "wma", "snd", "wav", "ra", "au", "aac", "exe", "msi"],
        100 * 1024,             # 100 KB
        2 * 1024 * 1024         # 2 MB
    ),
    "Light": (
        ["txt", "rtf", "docx", "csv", "doc", "wps", "wpd", "msg", "c", "ccp", "java", "py", "js", "ts", "cs", "swift", "dta", "pl", "sh", "bat", "com", "html", "htm", "xhtml", "asp", "css", "aspx", "rss"],
        1 * 1024,               # 1 KB
        100 * 1024              # 100 KB
    )
}

TIERS = [1, 10, 50, 100]
BASE_DIR = r"D:\PocketDrop_TrueData_Tests"
CHUNK_SIZE = 5 * 1024 * 1024  # 5 MB chunks to protect RAM

# ==========================================
# 2. TRUE DATA GENERATOR LOGIC
# ==========================================

def create_true_file(filepath, target_size):
    """Writes actual random bytes to the file in safe chunks."""
    with open(filepath, "wb") as f:
        bytes_written = 0
        while bytes_written < target_size:
            write_size = min(CHUNK_SIZE, target_size - bytes_written)
            f.write(os.urandom(write_size))
            bytes_written += write_size

print(f"Starting Tiered TRUE DATA Generation in {BASE_DIR}...")
print("WARNING: Writing ~22 GB of real random data will take significant time!\n")

os.makedirs(BASE_DIR, exist_ok=True)
total_files_created = 0
total_bytes = 0

for tier_amount in TIERS:
    tier_dir = os.path.join(BASE_DIR, f"Tier_{tier_amount:03d}_Files")
    os.makedirs(tier_dir, exist_ok=True)
    
    print(f"---> Building {tier_dir}...")
    
    for weight_class, (extensions, min_size, max_size) in CATEGORIES.items():
        for ext in extensions:
            for i in range(1, tier_amount + 1):
                size = random.randint(min_size, max_size)
                total_bytes += size
                filename = os.path.join(tier_dir, f"test_{ext}_{i:03d}.{ext}")
                
                # ✨ THE DIFFERENCE: Writing physical bytes instead of truncating
                create_true_file(filename, size)
                    
                total_files_created += 1

print("\n==========================================")
print(f"DONE! Successfully generated {total_files_created} TRUE DATA files.")
print(f"Total Real Data Written: {(total_bytes / (1024**3)):.2f} GB")
print(f"Your test files are located in: {BASE_DIR}")
print("==========================================")