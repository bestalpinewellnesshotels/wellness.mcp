const path = require('path');

module.exports = {
  entry: './src/chatbot.ts',
  module: {
    rules: [
      {
        test: /\.ts$/,
        use: 'ts-loader',
        exclude: /node_modules/,
      },
      {
        test: /\.css$/,
        use: ['style-loader', 'css-loader'],
      },
    ],
  },
  resolve: {
    extensions: ['.ts', '.js'],
  },
  output: {
    filename: 'chatbot.js',
    path: path.resolve(__dirname, 'dist'),
    library: {
      type: 'umd',
      name: 'HotelChatbot',
    },
    globalObject: 'this',
  },
  devServer: {
    static: {
      directory: __dirname,
    },
    compress: true,
    port: 8080,
    open: false,
  },
};
