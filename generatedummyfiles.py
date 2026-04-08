import os
import random

# ==========================================
# 1. THE 62 FORMATS & SCALED DOWN SIZES
# ==========================================
# To fit 10,000 files under 100GB, we scale the max sizes down.

CATEGORIES = {
    "Heavy": (
        ["mp4", "3gp", "avi", "mpg", "mov", "wmv", "rar", "zip", "hqx", "arj", "tar", "arc", "sit", "gz", "z"],
        1 * 1024 * 1024,        # 1 MB
        15 * 1024 * 1024        # 15 MB (Max combined Heavy size ~ 36 GB)
    ),
    "Medium": (
        ["jpg", "png", "webp", "gif", "tif", "bmp", "eps", "mp3", "wma", "snd", "wav", "ra", "au", "aac", "exe", "msi"],
        100 * 1024,             # 100 KB
        2 * 1024 * 1024         # 2 MB (Max combined Medium size ~ 5 GB)
    ),
    "Light": (
        ["txt", "rtf", "docx", "csv", "doc", "wps", "wpd", "msg", "c", "ccp", "java", "py", "js", "ts", "cs", "swift", "dta", "pl", "sh", "bat", "com", "html", "htm", "xhtml", "asp", "css", "aspx", "rss"],
        1 * 1024,               # 1 KB
        100 * 1024              # 100 KB (Max combined Light size ~ 0.5 GB)
    )
}

TIERS = [1, 10, 50, 100]
BASE_DIR = r"D:\PocketDrop_Format_Tests"

# ==========================================
# 2. GENERATOR LOGIC
# ==========================================

print("Starting Tiered Dummy File Generation...")
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
                
                with open(filename, "wb") as f:
                    f.truncate(size)
                    
                total_files_created += 1

print("\n==========================================")
print(f"DONE! Successfully generated {total_files_created} total files.")
print(f"Total Space Reserved: {(total_bytes / (1024**3)):.2f} GB")
print(f"Your test files are located in: {BASE_DIR}")
print("==========================================")