import numpy as np
import cv2
#import os

#os.remove("cam.png") 

cam_port = 0
cam = cv2.VideoCapture(cam_port) 
result, image = cam.read() 

if result: 
    cv2.imwrite("cam.png", image) 
else: 
    print("No image detected") 