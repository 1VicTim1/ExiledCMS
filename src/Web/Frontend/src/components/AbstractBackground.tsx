'use client';

import { motion } from 'framer-motion';

export function AbstractBackground() {
  return (
    <div className="fixed inset-0 z-0 overflow-hidden bg-background pointer-events-none">
      {/* Dark gradient overlay */}
      <div className="absolute inset-0 bg-gradient-to-br from-background via-background/90 to-background/50 z-10 pointer-events-none" />
      
      {/* Animated abstract shapes */}
      <motion.div
        className="absolute top-[-10%] left-[-10%] w-[40%] h-[40%] rounded-full bg-primary/20 blur-[120px] pointer-events-none"
        animate={{
          x: [0, 100, 0],
          y: [0, 50, 0],
          scale: [1, 1.2, 1],
        }}
        transition={{
          duration: 15,
          repeat: Infinity,
          ease: 'easeInOut',
        }}
      />
      
      <motion.div
        className="absolute bottom-[-10%] right-[-10%] w-[50%] h-[50%] rounded-full bg-purple-900/20 blur-[150px] pointer-events-none"
        animate={{
          x: [0, -100, 0],
          y: [0, -50, 0],
          scale: [1, 1.5, 1],
        }}
        transition={{
          duration: 20,
          repeat: Infinity,
          ease: 'easeInOut',
        }}
      />

      <motion.div
        className="absolute top-[40%] left-[60%] w-[30%] h-[30%] rounded-full bg-indigo-900/10 blur-[100px] pointer-events-none"
        animate={{
          x: [0, -50, 0],
          y: [0, 100, 0],
          scale: [1, 1.1, 1],
        }}
        transition={{
          duration: 18,
          repeat: Infinity,
          ease: 'easeInOut',
        }}
      />
      
      {/* Subtle grid pattern */}
      <div 
        className="absolute inset-0 z-10 opacity-[0.03] [mask-image:linear-gradient(to_bottom,white,transparent)] pointer-events-none" 
        style={{
          backgroundImage: 'radial-gradient(circle at center, #ffffff 1px, transparent 1px)',
          backgroundSize: '24px 24px',
        }}
      />
    </div>
  );
}
