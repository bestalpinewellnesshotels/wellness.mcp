import resolve from '@rollup/plugin-node-resolve';
import typescript from '@rollup/plugin-typescript';
import { terser } from 'rollup-plugin-terser';

export default {
  input: 'src/chatbot.ts',
  output: {
    file: 'dist/chatbot.js',
    format: 'iife',
    name: 'HotelChatbot',
    sourcemap: true
  },
  plugins: [
    resolve(),
    typescript({
      tsconfig: './tsconfig.json',
      sourceMap: true,
      inlineSources: true
    }),
    terser({
      compress: {
        drop_console: false
      }
    })
  ]
};
