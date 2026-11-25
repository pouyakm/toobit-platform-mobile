import type { Messages } from './en'

export const fa: Messages = {
  appName: 'پلتفرم توبیت',
  nav: { scanner: 'اسکنر', positions: 'پوزیشن‌ها', orders: 'سفارش‌ها', signals: 'سیگنال‌ها', risk: 'ریسک و گزارش‌ها', settings: 'تنظیمات' },
  common: { loading: 'در حال بارگذاری…', error: 'مشکلی رخ داد', confirm: 'تأیید', cancel: 'انصراف', logout: 'خروج', language: 'زبان', none: 'هنوز چیزی برای نمایش نیست' },
  login: { title: 'ورود', email: 'ایمیل', password: 'گذرواژه', submit: 'ورود', failed: 'ورود ناموفق بود. اطلاعات را بررسی کنید.' },
  banner: {
    marketStale: 'داده‌های بازار قدیمی هستند. قیمت‌ها ممکن است به‌روز نباشند.',
    scannerStale: 'اسکنر قدیمی است. آخرین رتبه‌بندی معتبر نمایش داده می‌شود.',
    wsDown: 'اتصال قیمت زنده قطع است. در حال اتصال مجدد…',
    killOn: 'کلید توقف اضطراری روشن است. ثبت سفارش متوقف شده است.',
  },
  scanner: {
    title: 'نمادهای پرروند', rank: 'رتبه', symbol: 'نماد', score: 'امتیاز', volume: 'حجم', momentum: 'مومنتوم', spike: 'جهش حجم',
    price: 'قیمت زنده', pin: 'سنجاق', unpin: 'برداشتن سنجاق', why: 'توضیح امتیاز', breakdown: 'تفکیک هر معیار (نرمال‌شده ۰ تا ۱)',
  },
  positions: { title: 'پوزیشن‌های باز', symbol: 'نماد', side: 'سمت', qty: 'مقدار', entry: 'ورود', close: 'بستن پوزیشن', closeConfirm: 'پوزیشن {symbol} با سفارش بازار بسته شود؟ این یک سفارش فقط‌کاهشی است و ثبت می‌شود.' },
  orders: { title: 'سفارش‌ها', symbol: 'نماد', state: 'وضعیت', created: 'ایجاد', trail: 'مسیر وضعیت', retries: 'تلاش‌های وضعیت نامشخص' },
  signals: { title: 'سیگنال‌ها', action: 'اقدام', confidence: 'اطمینان', model: 'مدل', shadow: 'سایه', features: 'ویژگی‌های اثرگذار', note: 'اطمینان تقریبی است و احتمال کالیبره‌شده نیست.' },
  risk: { title: 'رویدادهای ریسک', recon: 'رویدادهای تطبیق', rule: 'قانون', reason: 'دلیل', kind: 'نوع', paused: 'نماد متوقف شد', pausedList: 'نمادهای متوقف‌شده توسط تطبیق', clearPause: 'رفع توقف' },
  settings: {
    title: 'تنظیمات استراتژی و اسکنر', killTitle: 'کلید توقف اضطراری', killOn: 'توقف ثبت همه سفارش‌ها', killOff: 'ازسرگیری ثبت سفارش',
    killHint: 'برای تأیید {word} را تایپ کنید.', reason: 'دلیل', flags: 'پرچم‌های ویژگی', autoTrading: 'معاملات خودکار', weightSet: 'مجموعه وزن اسکنر', save: 'ذخیره پرچم‌ها',
    readonlyNote: 'محدودیت‌های عددی استراتژی و اسکنر در این نسخه از فایل پیکربندی خوانده می‌شوند و فقط‌خواندنی‌اند.',
  },
}
