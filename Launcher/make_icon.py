from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

size = 256
img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
draw.rounded_rectangle((12, 12, 244, 244), radius=52, fill=(255, 255, 255, 255),
                       outline=(30, 35, 45, 255), width=8)
font = ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 168)
bbox = draw.textbbox((0, 0), "Ш", font=font)
w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
x = (size - w) // 2 - bbox[0]
y = (size - h) // 2 - bbox[1] - 5
draw.text((x, y), "Ш", fill=(28, 34, 44, 255), font=font)
img.save(Path(__file__).with_name("shishani.ico"), format="ICO",
         sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
