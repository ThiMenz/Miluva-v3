import numpy as np
import cv2

cam_port = 0
cam = cv2.VideoCapture(cam_port) 
result, image = cam.read() 

if result: 
    cv2.imwrite("cam2.png", image) 
else: 
    print("No image detected") 